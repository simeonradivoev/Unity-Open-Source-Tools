using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Events;

public class EventManager : MonoBehaviour
{
	private static EventManager _instance;
	private Dictionary<Type, AbstractEventHandler> eventListeners;
	private List<QueuedListener> queuedListeners;
	private List<QueuedListener> queuedRemoveListeners;

	public static void Create()
	{
		if (CheckIfEditor()) return;
		_instance = new GameObject("_EventManager_").AddComponent<EventManager>();
		DontDestroyOnLoad(_instance);
	}

	[UsedImplicitly]
	private void Awake()
	{
		if (eventListeners == null) eventListeners = new Dictionary<Type, AbstractEventHandler>();
		if (queuedListeners == null) queuedListeners = new List<QueuedListener>();
		if (queuedRemoveListeners == null) queuedRemoveListeners = new List<QueuedListener>();
	}

	//A global validate method for removing drifting listeners with destroyed (null) targets
	public void Validate()
	{
		foreach (var listener in eventListeners.Values)
		{
			listener.Validate();
		}
	}

	[UsedImplicitly]
	private void LateUpdate()
	{
		while (queuedRemoveListeners.Count > 0)
		{
			QueuedListener queuedListener = queuedRemoveListeners[queuedRemoveListeners.Count - 1];
			queuedRemoveListeners.RemoveAt(queuedRemoveListeners.Count - 1);
			AbstractEventHandler eventHandler;
			if (Instance.eventListeners.TryGetValue(queuedListener.eventType, out eventHandler))
			{
				eventHandler.RemoveListener(queuedListener.internalDelegate, queuedListener.onEvent, queuedListener.args);
			}
		}

		while (queuedListeners.Count > 0)
		{
			QueuedListener queuedListener = queuedListeners[0];
			queuedListeners.RemoveAt(0);
			ProcessQueuedListener(queuedListener);
		}
	}

	private void ProcessQueuedListener(QueuedListener queuedListener)
	{
		AbstractEventHandler eventHandler;
		if (Instance.eventListeners.TryGetValue(queuedListener.eventType, out eventHandler))
		{
			eventHandler.AddListener(queuedListener.internalDelegate, queuedListener.onEvent, queuedListener.handle, queuedListener.args);
		}
		else
		{
			RegisterEvent(queuedListener.eventType).AddListener(queuedListener.internalDelegate, queuedListener.onEvent, queuedListener.handle, queuedListener.args);
		}
	}

	public static T GetHandler<T>(Type type) where T : AbstractEventHandler
	{
		return (T)Instance.eventListeners[type];
	}

	public static AbstractEventHandler RegisterEvent<T>() where T : Event
	{
		return RegisterEvent(typeof(T));
	}

	public static void RegisterEvent<T>(AbstractEventHandler listener) where T : Event
	{
		RegisterEvent(typeof(T), listener);
	}

	public static AbstractEventHandler RegisterEvent(Type type)
	{
		CheckEventType(type);
		EventHandler eventHandler = new EventHandler();
		return RegisterEvent(type, eventHandler);
	}

	public static AbstractEventHandler RegisterEvent(Type type, AbstractEventHandler mainListener)
	{
		CheckEventType(type);
		if (Instance.eventListeners.ContainsKey(type))
		{
			Debug.LogWarningFormat("Event Handler for type {0} is already defined, replacing with new handler. All listeners will be transferred", type.FullName);
			mainListener.Import(Instance.eventListeners[type]);
			Instance.eventListeners[type] = mainListener;
		}
		else
		{
			Instance.eventListeners.Add(type, mainListener);
		}
		return mainListener;
	}

	public static T InvokeEvent<T>(T @event) where T : Event
	{
		return (T)InvokeEvent(typeof(T), @event);
	}

	public static Event InvokeEvent(Type eventType, Event @event)
	{
		if (CheckIfEditor()) return @event;
		AbstractEventHandler handler;
		Register:
		if (Instance.eventListeners.TryGetValue(eventType, out handler))
		{
			handler.Invoke(@event);

			if (!(eventType.BaseType == typeof(Event) || eventType.BaseType == typeof(object)))
			{
				@event = InvokeEvent(eventType.BaseType, @event);
			}
		}
		//process any listeners that are queued and will otherwise be missed
		//and then retry the registration again
		else if (Instance.queuedListeners.Any(a => a.eventType == eventType))
		{
			foreach (var queuedListener in Instance.queuedListeners)
			{
				Instance.ProcessQueuedListener(queuedListener);
			}
			Instance.queuedListeners.RemoveAll(a => a.eventType == eventType);
			//try the registration again
			goto Register;
		}
		//register the event handler if no queued or available handlers were found
		else
		{
			RegisterEvent(eventType);
			InvokeEvent(eventType, @event);
		}

		return @event;
	}

	public static void AddListiner(Type type, AbstractEventHandler.OnEvent onEvent, params object[] arg)
	{
		AddListiner(type, onEvent, onEvent.Target as UnityEngine.Object, arg);
	}

	public static void AddListiner(Type type, AbstractEventHandler.OnEvent onEvent, UnityEngine.Object handle, params object[] arg)
	{
		if (CheckIfEditor()) return;
		//clear to be removed if being added again
		if (Instance.queuedRemoveListeners.Any(l => l.onEvent == onEvent))
		{
			Instance.queuedRemoveListeners.RemoveAll(l => l.onEvent == onEvent);
		}
		else
		{
			AbstractEventHandler eventHandler;
			if (Instance.eventListeners.TryGetValue(type, out eventHandler))
			{
				eventHandler.AddListener(null, onEvent, handle, arg);
				return;
			}
			Instance.queuedListeners.Add(new QueuedListener(type, null, onEvent, handle, arg));
		}
	}

	public static void AddListiner<T>(AbstractEventHandler.OnEvent<T> onEvent, UnityEngine.Object handle, params object[] arg) where T : Event
	{
		if (CheckIfEditor()) return;

		//clear to be removed if being added again
		if (Instance.queuedRemoveListeners.RemoveAll(l => l.internalDelegate == onEvent) <= 0)
		{
			//add the listener to the event handler
			AbstractEventHandler eventHandler;
			if (Instance.eventListeners.TryGetValue(typeof(T), out eventHandler))
			{
				eventHandler.AddListener(onEvent, WrapListener(onEvent), handle, arg);
				return;
			}
			Instance.queuedListeners.Add(new QueuedListener(typeof(T), onEvent, WrapListener(onEvent), handle, arg));
		}
	}

	public static void AddListiner<T>(AbstractEventHandler.OnEvent<T> onEvent, params object[] arg) where T : Event
	{
		AddListiner(onEvent, onEvent.Target as UnityEngine.Object, arg);
	}

	public static void AddListenerToHandler<T>(AbstractEventHandler handler, AbstractEventHandler.OnEvent<T> onEvent, params object[] arg) where T : Event
	{
		if (CheckIfEditor()) return;

		//clear to be removed if being added again
		if (Instance.queuedRemoveListeners.RemoveAll(l => l.internalDelegate == onEvent) <= 0)
		{
			//add the listener to the event handler
			handler.AddListener(onEvent, WrapListener(onEvent), onEvent.Target as UnityEngine.Object, arg);
		}
	}

	public static AbstractEventHandler.OnEvent WrapListener<T>(AbstractEventHandler.OnEvent<T> onEvent) where T : Event
	{
		return e => onEvent((T)e);
	}

	public static void RemoveListiner<T>(AbstractEventHandler.OnEvent<T> onEvent, params object[] arg) where T : Event
	{
		if (CheckIfEditor()) return;
		Instance.queuedRemoveListeners.Insert(0, new QueuedListener(typeof(T), onEvent, null, null, arg));
	}

	public static void RemoveListiner(Type type, AbstractEventHandler.OnEvent onEvent, params object[] arg)
	{
		if (CheckIfEditor()) return;
		Instance.queuedRemoveListeners.Insert(0, new QueuedListener(type, null, onEvent, null, arg));
	}

	private static void CheckEventType([NotNull] Type type)
	{
		if (type == null) throw new ArgumentNullException("type");
		if (!typeof(Event).IsAssignableFrom(type))
		{
			throw new UnityException("Type must derive from " + typeof(Event).FullName);
		}
	}

	public static EventManager Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = FindObjectOfType<EventManager>();
			}
			if (_instance == null)
			{
				Create();
			}
			return _instance;
		}
	}

	public class Event
	{
		private bool used;

		public void Use()
		{
			used = true;
		}

		public bool Used
		{
			get { return used; }
		}

		public virtual void Reset()
		{
			used = false;
		}
	}

	#region Wrappers

	public class ListenerWrapper
	{
		private AbstractEventHandler.OnEvent callback;
		private bool hasHandle;
		private UnityEngine.Object handle;

		public ListenerWrapper(AbstractEventHandler.OnEvent callback, UnityEngine.Object handle)
		{
			this.callback = callback;
			if (handle != null)
			{
				hasHandle = true;
				this.handle = handle;
			}
		}

		public AbstractEventHandler.OnEvent Callback
		{
			get { return callback; }
		}

		public UnityEngine.Object Handle
		{
			get { return handle; }
		}

		public bool IsValid
		{
			get
			{
				if (hasHandle && !handle)
				{
					return false;
				}
				if (callback.Target == null)
				{
					return false;
				}
				return true;
			}
		}
	}

	public class ListenerWrapperWeighted : ListenerWrapper, IComparable<ListenerWrapperWeighted>
	{
		private readonly int weight;

		public ListenerWrapperWeighted(AbstractEventHandler.OnEvent callback, UnityEngine.Object handle, int weight) : base(callback, handle)
		{
			this.weight = weight;
		}

		public int CompareTo(ListenerWrapperWeighted other)
		{
			if (other == null || !other.IsValid) return 0;
			return weight.CompareTo(other.weight);
		}
	}

	#endregion

	#region Event Handler

	public abstract class EventHandlerBase<T> : AbstractEventHandler where T : ListenerWrapper
	{
		public readonly List<T> eventListeners = new List<T>();
		public readonly Dictionary<Delegate, T> delegateLookup = new Dictionary<Delegate, T>();

		public override void AddListener(Delegate internalDelegate, OnEvent onEvent, UnityEngine.Object handle, params object[] arg)
		{
			if (internalDelegate != null && delegateLookup.ContainsKey(internalDelegate))
			{
				ThrowListenerPresent(internalDelegate, onEvent);
				return;
			}

			if (eventListeners.Any(l => l.Callback == onEvent))
			{
				ThrowListenerPresent(internalDelegate, onEvent);
				return;
			}

			var wrapper = CreateWrapper(internalDelegate, onEvent, handle);
			eventListeners.Add(wrapper);
			OnListenerAdded(internalDelegate, wrapper);
			if (internalDelegate != null) delegateLookup.Add(internalDelegate, wrapper);
		}

		protected abstract T CreateWrapper(Delegate internalDelegate, OnEvent onEvent, UnityEngine.Object handle, params object[] arg);
		protected abstract void OnListenerAdded(Delegate internalDelegate, T wrapper);

		public override void RemoveListener(Delegate internalDelegate, OnEvent onEvent, params object[] arg)
		{
			T eventWrapper;
			if (internalDelegate != null && delegateLookup.TryGetValue(internalDelegate, out eventWrapper))
			{
				delegateLookup.Remove(internalDelegate);
				eventListeners.Remove(eventWrapper);
			}
			else
			{
				eventListeners.RemoveAll(e => e.Callback == onEvent);
			}
		}

		public override void Invoke(Event @event)
		{
			int index = 0;
			while (index < eventListeners.Count)
			{
				try
				{
					eventListeners[index].Callback.Invoke(@event);
				}
				catch (Exception e)
				{
					var keyValue = delegateLookup.FirstOrDefault(d => d.Value == eventListeners[index]);
					Delegate evnt = eventListeners[index].Callback;
					if (keyValue.Key != null)
					{
						evnt = keyValue.Key;
					}
					Debug.LogError("There was a problem while executing an event on object: " + evnt.Target);
					Debug.LogException(e);
				}
				index++;
				if (@event.Used)
				{
					break;
				}
			}
		}

		public override void Validate()
		{
			for (int i = eventListeners.Count - 1; i >= 0; i--)
			{
				if (!eventListeners[i].IsValid)
				{
					var keysToRemove = delegateLookup.Where(e => e.Value == eventListeners[i]).Select(e => e.Key).ToArray();
					foreach (var key in keysToRemove)
					{
						delegateLookup.Remove(key);
					}
					eventListeners.RemoveAt(i);
				}
			}
		}
	}

	public class EventHandler : EventHandlerBase<ListenerWrapper>
	{
		public override void Import(AbstractEventHandler @from)
		{
			EventHandler EventHandler = (EventHandler)from;
			if (EventHandler != null)
			{
				eventListeners.AddRange(EventHandler.eventListeners);
			}
		}

		protected override ListenerWrapper CreateWrapper(Delegate internalDelegate, OnEvent onEvent, UnityEngine.Object handle, params object[] arg)
		{
			return new ListenerWrapper(onEvent, handle);
		}

		protected override void OnListenerAdded(Delegate internalDelegate, ListenerWrapper wrapper)
		{

		}
	}

	public class WeightedEventHandler<T> : EventHandlerBase<ListenerWrapperWeighted> where T : Event
	{
		public override void Import(AbstractEventHandler from)
		{
			if (from is WeightedEventHandler<T>)
			{
				eventListeners.AddRange(((WeightedEventHandler<T>)from).eventListeners);
			}
			else if (from is EventHandler)
			{
				EventHandler EventHandler = (EventHandler)from;
				foreach (var eventListener in EventHandler.eventListeners)
				{
					eventListeners.Add(new ListenerWrapperWeighted(eventListener.Callback, eventListener.Handle, 1));
				}
			}
		}

		protected override ListenerWrapperWeighted CreateWrapper(Delegate internalDelegate, OnEvent onEvent, UnityEngine.Object handle, params object[] arg)
		{
			if (arg.Length > 0 && arg[0] is int)
			{
				return new ListenerWrapperWeighted(onEvent, handle, (int)arg[0]);
			}
			return new ListenerWrapperWeighted(onEvent, handle, 0);
		}

		public void AddListener(OnEvent<T> onEvent, UnityEngine.Object handle, int weight)
		{
			EventManager.AddListenerToHandler(this, onEvent, handle, weight);
		}

		public void AddListener(OnEvent<T> onEvent, int weight)
		{
			EventManager.AddListenerToHandler(this, onEvent, weight);
		}

		protected override void OnListenerAdded(Delegate internalDelegate, ListenerWrapperWeighted wrapper)
		{
			eventListeners.Sort();
		}
	}

	public abstract class AbstractEventHandler
	{
		public delegate void OnEvent<T>(T eEvent) where T : Event;
		public delegate void OnEvent(Event eEvent);
		public abstract void Invoke(Event @event);
		public abstract void AddListener(Delegate internalDelegate, OnEvent onEvent, UnityEngine.Object handle, params object[] arg);
		public abstract void RemoveListener(Delegate internalDelegate, OnEvent onEvent, params object[] arg);
		public abstract void Import(AbstractEventHandler from);
		public abstract void Validate();

		protected void ThrowListenerPresent(Delegate internalDelegate, OnEvent evnt)
		{
#if THROW_PRESENT_LISTENER_ERRROR
			if (internalDelegate != null)
			{
				Debug.LogWarningFormat(internalDelegate.Target as UnityEngine.Object, "Event Listener for Event '{0}' in object: {1} already registered", internalDelegate, internalDelegate.Target);
			}
			else
			{
				Debug.LogWarningFormat(evnt.Target as UnityEngine.Object, "Event Listener for Event '{0}' in object: {1} already registered", evnt, evnt.Target);
			}
#endif
		}

		protected bool IsTargetValid(object obj)
		{
			return obj != null || (obj is UnityEngine.Object && ((UnityEngine.Object)obj));
		}
	}

	#endregion

	private class QueuedListener
	{
		public readonly Type eventType;
		public readonly UnityEngine.Object handle;
		public readonly object[] args;
		public readonly Delegate internalDelegate;
		public readonly AbstractEventHandler.OnEvent onEvent;

		public QueuedListener(Type eventType, Delegate internalDelegate, AbstractEventHandler.OnEvent onEvent, UnityEngine.Object handle, object[] args)
		{
			this.eventType = eventType;
			this.args = args;
			this.internalDelegate = internalDelegate;
			this.onEvent = onEvent;
			this.handle = handle;
		}
	}

	public static bool CheckIfEditor()
	{
#if UNITY_EDITOR
		if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return true;
#endif
		return false;
	}

	public class EventListener : UnityEvent<Event>
	{

	}
}