using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Wizards;
using Object = UnityEngine.Object;

public class PrefabBrowser : EditorWindow
{
	private static PrefabBrowser instance;

	public static PrefabBrowser Instance
	{
		get { return instance; }
	}

	#region Constants

	public const string PrefabType = "GameObject";

	#endregion

	public Rect topToolbarRect
	{
		get { return new Rect(0, 0, position.width, EditorGUIUtility.singleLineHeight); }
	}

	public Rect sideRect
	{
		get { return new Rect(EditorGUIUtility.singleLineHeight, EditorGUIUtility.singleLineHeight + 2, sideWidth, position.height - topToolbarRect.height - footerRect.height); }
	}

	public Rect prefabListRect
	{
		get { return new Rect(sideRect.width + 32, EditorGUIUtility.singleLineHeight + 2, position.width - sideRect.width - 32, position.height - topToolbarRect.height - footerRect.height); }
	}

	public Rect footerRect
	{
		get { return new Rect(0, position.height - 24, position.width, 24); }
	}

	private int selectedPrefabId;
	private HashSet<string> selectedGuids = new HashSet<string>();
	private Vector2 scroll;
	private Vector2 labelsScroll;
	private Rect contentRect;
	private List<PrefabInfo> prefabsList = new List<PrefabInfo>();
	private List<string> labels = new List<string>();
	private HashSet<string> selectedLabels = new HashSet<string>();
	private float sideWidth = 128;
	private Rect addLabelButtonRect;
	private Settings settings = new Settings();
	private RaycastHit lastRaycast;
	private bool settingsOpen;

	#region Dragging

	private GameObject draggedGameObject;
	private Vector3 dragRotation;
	private Vector2 lastDragMousePosition;

	#endregion

	#region Painting

	private GameObject lastSelectedPrefab;
	private GameObject paintObject;
	private Vector3 paintRotation;

	#endregion

	private class Settings
	{
		public string search = string.Empty;
		public float prefabElementSizeMultiply = 1;
		public bool selectInInspector = true;
		public bool multiSelection = true;
		public bool pivot;
		public bool paint;
		public bool followSurface;
		public string prefabLabel = "Prefab";
		public float maxPrefabElementSize = 128f;
	}

	#region Reflected Calls

	private FieldInfo ignoreRaySnapObjects;

	#endregion

	#region Styles

	private GUIStyle prefabElementStyle;

	#endregion

	[MenuItem("Window/Prefab Browser")]
	public static void CreateEditor()
	{
		PrefabBrowser browser = GetWindow<PrefabBrowser>();
		browser.titleContent = new GUIContent("Prefabs", EditorGUIUtility.FindTexture("LODGroup Icon"));
		instance = browser;
	}

	#region Unity Calls

	private void OnEnable()
	{
		Selection.selectionChanged += Repaint;

		LoadData();
		LoadPrefabInfos();
		instance = this;

		ignoreRaySnapObjects = typeof (HandleUtility).GetField("ignoreRaySnapObjects", BindingFlags.NonPublic | BindingFlags.Static);
	}

	private void OnDisable()
	{
		SaveData();
	}

	private void OnDestory()
	{
		SaveData();
		SceneView.onSceneGUIDelegate -= OnSceneGUI;
	}

	private void OnGUI()
	{
		LoadStyles();
		Event current = Event.current;
		DrawToolbar(current);
		if (settingsOpen)
		{
			DrawSettings(current);
		}
		else
		{
			DrawPrefabList(current);
		}
		DrawSide(current);
		DrawFooter(current);
	}

	private void OnFocus()
	{
		LoadData();

		// Remove delegate listener if it has previously
		// been assigned.
		SceneView.onSceneGUIDelegate -= OnSceneGUI;
		// Add (or re-add) the delegate.
		SceneView.onSceneGUIDelegate += OnSceneGUI;
	}

	private void OnLostFocus()
	{
		SaveData();
	}

	#endregion

	

	#region MenuCallbacks

	private void ReplaceWithPrefabCallback(object GuidObject)
	{
		string guid = (string) GuidObject;
		GameObject prefab = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), typeof (Object)) as GameObject;
		ReplaceWithPrefab.Replace(prefab);
	}

	private void AddAllLabelsCallback(object labelObjects)
	{
		foreach (var label in (string[]) labelObjects)
		{
			if (string.Equals(label, settings.prefabLabel, StringComparison.InvariantCultureIgnoreCase)) continue;
			labels.Add(label);
		}
		labels.Sort();
		Repaint();
	}

	private void AddLabelCallback(object labelObject)
	{
		string label = (string) labelObject;
		if (string.Equals(label, settings.prefabLabel, StringComparison.InvariantCultureIgnoreCase)) return;
		labels.Add(label);
		labels.Sort();
		Repaint();
	}

	private void RemoveLabelCallback(object labelObject)
	{
		labels.Remove((string) labelObject);
		Repaint();
	}

	private void RemoveAllLabelsCallback()
	{
		if (EditorUtility.DisplayDialog("Remove All Labels", "Are you sure you want to remove all labels", "Remove All", "Cancel"))
		{
			labels.Clear();
			Repaint();
		}
	}

	private void ToggleLabelCallback(object labelObject)
	{
		if (labelObject == null)
		{
			selectedLabels.Clear();
			LoadPrefabInfos();
			return;
		}
		string label = (string) labelObject;
		bool selected = selectedLabels.Contains(label);
		if (selected)
		{
			selectedLabels.Remove(label);
		}
		else
		{
			selectedLabels.Add(label);
		}
		LoadPrefabInfos();
	}

	#endregion

	#region Dragging and Painting

	//Used for Prefab placing in scene
	private void OnSceneGUI(SceneView sceneView)
	{
		Event current = Event.current;

		#region Painting

		if (settings.paint)
		{
			//disable selection control in scene when painting
			int controlID = GUIUtility.GetControlID(FocusType.Passive);
			// ... gui stuff
			if (current.type == EventType.Layout)
			{
				HandleUtility.AddDefaultControl(controlID);
			}

			if (lastSelectedPrefab != SelectedPrefab)
			{
				lastSelectedPrefab = SelectedPrefab;
				if (paintObject != null)
				{
					DestroyImmediate(paintObject);
					ignoreRaySnapObjects.SetValue(null, null);
					paintObject = null;
				}

				if (lastSelectedPrefab != null)
				{
					paintObject = CreatePrefabInstance(lastSelectedPrefab);
					ignoreRaySnapObjects.SetValue(null, paintObject.GetComponentsInChildren<Transform>());
				}

				sceneView.Repaint();
			}

			if (paintObject != null)
			{
				switch (current.type)
				{
					case EventType.Repaint:
						if (current.control || current.alt) Handles.DoRotationHandle(Quaternion.Euler(paintRotation), lastRaycast.point);
						Bounds paintBounds = GetRendererBounds(paintObject);
						Handles.DrawWireDisc(lastRaycast.point, lastRaycast.normal, Mathf.Max(paintBounds.extents.x, paintBounds.extents.z, paintBounds.extents.y));
						break;
					case EventType.KeyDown:
						if (current.keyCode == KeyCode.Space)
						{
							paintRotation = Vector3.zero;
							goto case EventType.MouseMove;
						}
						break;
					case EventType.ScrollWheel:
						if (current.control || current.alt)
						{
							float rotationSpeed = current.delta.y * 3;
							if (current.modifiers == EventModifiers.Control) paintRotation.y += rotationSpeed;
							else if (current.modifiers == EventModifiers.Alt) paintRotation.x += rotationSpeed;
							else if (current.modifiers == (EventModifiers.Control | EventModifiers.Alt)) paintRotation.z += rotationSpeed;
							current.Use();
						}
						goto case EventType.MouseMove;
					case EventType.MouseMove:
						if (GetSceneViewRaycast(ref lastRaycast, current))
						{
							//paintObjectBounds = paintObject.GetRendererBounds();
							PositionRaycastObject(lastRaycast, paintObject, Quaternion.Euler(paintRotation));
						}
						else
						{
							paintObject.transform.position = HandleUtility.GUIPointToWorldRay(current.mousePosition).GetPoint(10f);
						}
						sceneView.Repaint();
						current.Use();
						break;
					case EventType.MouseDown:
						if (current.button != 0) break;
						GameObject placedInstance = (GameObject) PrefabUtility.InstantiatePrefab(lastSelectedPrefab);
						string uniqueNameForSibling = GameObjectUtility.GetUniqueNameForSibling(null, placedInstance.name);
						placedInstance.transform.position = paintObject.transform.position;
						placedInstance.transform.rotation = paintObject.transform.rotation;
						if (Selection.activeTransform != null)
						{
							placedInstance.transform.SetParent(Selection.activeTransform, true);
						}
						placedInstance.name = uniqueNameForSibling;
						EditorUtility.SetDirty(placedInstance);
						Undo.RegisterCreatedObjectUndo(placedInstance, "Place " + placedInstance.name);
						current.Use();
						break;
				}
			}

			return;
		}

		//remove paint object if not painting
		if (paintObject != null || lastSelectedPrefab != null)
		{
			lastSelectedPrefab = null;
			DestroyImmediate(paintObject);
			ignoreRaySnapObjects.SetValue(null, null);
			paintObject = null;
			sceneView.Repaint();
		}

		#endregion

		#region Dragging

		if (DragAndDrop.GetGenericData("Prefab") != null && DragAndDrop.GetGenericData("Prefab") is GameObject)
		{
			if (current.type == EventType.DragUpdated)
			{
				if (draggedGameObject == null)
				{
					draggedGameObject = CreatePrefabInstance((GameObject) DragAndDrop.GetGenericData("Prefab"));
					ignoreRaySnapObjects.SetValue(null, draggedGameObject.GetComponentsInChildren<Transform>());
					PositionRaycastObject(lastRaycast, draggedGameObject, Quaternion.Euler(dragRotation));
				}

				DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
				if (current.control || current.alt)
				{
					//handle rotation of Object when holding CTRL
					Vector2 delta = lastDragMousePosition - current.mousePosition;
					lastDragMousePosition = current.mousePosition;
					if (current.modifiers == EventModifiers.Control) dragRotation.y += delta.x;
					else if (current.modifiers == EventModifiers.Alt) dragRotation.x += delta.x;
					else if (current.modifiers == (EventModifiers.Alt | EventModifiers.Control)) dragRotation.z += delta.x;
					PositionRaycastObject(lastRaycast, draggedGameObject, Quaternion.Euler(dragRotation));
				}
				else
				{
					lastDragMousePosition = current.mousePosition;
					if (current.shift)
					{
						dragRotation = Vector3.zero;
					}

					//position object on raycast hit point
					if (GetSceneViewRaycast(ref lastRaycast, current))
					{
						PositionRaycastObject(lastRaycast, draggedGameObject, Quaternion.Euler(dragRotation));
					}
					else
					{
						draggedGameObject.transform.position = HandleUtility.GUIPointToWorldRay(current.mousePosition).GetPoint(10f);
					}
				}

				if (sceneView.in2DMode)
				{
					Vector3 position = draggedGameObject.transform.position;
					position.z = PrefabUtility.FindPrefabRoot((GameObject) DragAndDrop.GetGenericData("Prefab")).transform.position.z;
					draggedGameObject.transform.position = position;
				}
			}
			else if (current.type == EventType.DragPerform)
			{
				string uniqueNameForSibling = GameObjectUtility.GetUniqueNameForSibling(null, draggedGameObject.name);
				draggedGameObject.hideFlags = HideFlags.None;
				Undo.RegisterCreatedObjectUndo(draggedGameObject, "Place " + draggedGameObject.name);
				EditorUtility.SetDirty(draggedGameObject);
				DragAndDrop.AcceptDrag();
				Selection.activeObject = draggedGameObject;
				ignoreRaySnapObjects.SetValue(null, null);
				mouseOverWindow.Focus();
				draggedGameObject.name = uniqueNameForSibling;
				draggedGameObject = null;
				current.Use();
			}
			else if (current.type == EventType.DragExited)
			{
				if (draggedGameObject)
				{
					DestroyImmediate(draggedGameObject, false);
					ignoreRaySnapObjects.SetValue(null, null);
					draggedGameObject = null;
					current.Use();
				}
			}
		}

		#endregion
	}

	private bool GetSceneViewRaycast(ref RaycastHit raycastHit, Event current)
	{
		object obj = HandleUtility.RaySnap(HandleUtility.GUIPointToWorldRay(current.mousePosition));
		if (obj == null)
		{
			return false;
		}
		raycastHit = (RaycastHit) obj;
		return true;
	}

	private Vector3 GetRaycastForward(RaycastHit raycastHit)
	{
		Vector3 forward = Vector3.Cross(Vector3.up, raycastHit.normal);
		if (forward == Vector3.zero)
		{
			forward = Vector3.forward;
		}
		forward.x = Mathf.Abs(forward.x);
		forward.y = Mathf.Abs(forward.y);
		forward.z = Mathf.Abs(forward.z);
		return forward;
	}

	private void PositionRaycastObject(RaycastHit raycastHit, GameObject instance, Quaternion rotation)
	{
		if (!settings.pivot)
		{
			//instance position and rotation need to be reset to properly calculate the bounds with just the custom rotation
			instance.transform.position = Vector3.zero;
			instance.transform.rotation = rotation;
			Bounds renderBounds = GetRendererBounds(instance);

			instance.transform.rotation = Quaternion.LookRotation(GetRaycastForward(raycastHit), raycastHit.normal) * rotation;
			instance.transform.position = raycastHit.point + instance.transform.position - renderBounds.center;
			if (settings.followSurface)
			{
				instance.transform.position += raycastHit.normal * renderBounds.extents.y;
			}
		}
		else
		{
			instance.transform.rotation = Quaternion.LookRotation(GetRaycastForward(raycastHit), raycastHit.normal) * rotation;
			instance.transform.position = raycastHit.point;
		}
	}

	private GameObject CreatePrefabInstance(GameObject prefab)
	{
		GameObject instance = (GameObject) PrefabUtility.InstantiatePrefab(PrefabUtility.FindPrefabRoot(prefab));
		instance.hideFlags = HideFlags.HideAndDontSave;
		return instance;
	}

	#endregion

	#region Context Menu Generation

	private void GeneratePrefabContextMenu(GenericMenu contextMenu,PrefabInfo prefabGuid)
	{
		string[] assetLabels = AssetDatabase.GetLabels(AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(prefabGuid.Guid),typeof(Object)));
		contextMenu.AddItem(new GUIContent("Test User Data"), false, () =>
		{
			string p = AssetDatabase.GUIDToAssetPath(prefabGuid.Guid);
			Debug.Log(p);
			AssetImporter.GetAtPath(p).userData = "Test Data";
			AssetImporter.GetAtPath(p).SaveAndReimport();
		});
		if (Selection.GetFiltered(typeof(GameObject), SelectionMode.ExcludePrefab).Cast<GameObject>().Any())
			contextMenu.AddItem(new GUIContent("Replace", "Replace selected instance in scene with this prefab"), false, ReplaceWithPrefabCallback, prefabGuid.Guid);
		else
			contextMenu.AddDisabledItem(new GUIContent("Replace", "Replace selected instance in scene with this prefab"));
		contextMenu.AddItem(new GUIContent("Labels/Add All"), false, AddAllLabelsCallback, assetLabels);
		foreach (var assetLabel in assetLabels)
		{
			if (string.Equals(assetLabel, settings.prefabLabel, StringComparison.InvariantCultureIgnoreCase)) continue;
			contextMenu.AddItem(new GUIContent("Labels/Add/" + assetLabel), false, AddLabelCallback, assetLabel);
		}
		contextMenu.AddItem(new GUIContent("Reimport"), false, () => {
			AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(prefabGuid.Guid));
		});
		contextMenu.AddItem(new GUIContent("Show In Explorer"), false, (obj) => { EditorUtility.RevealInFinder(AssetDatabase.GUIDToAssetPath((string)obj)); }, prefabGuid.Guid);
		contextMenu.ShowAsContext();
	}

	private void GenerateLabelsMenu(GenericMenu genericMenu)
	{
		foreach (var label in labels)
		{
			genericMenu.AddItem(new GUIContent("Remove/" + label), false, RemoveLabelCallback, label);
		}
		genericMenu.AddItem(new GUIContent("Show/All"), selectedLabels.Count <= 0, ToggleLabelCallback, null);
		foreach (var label in labels)
		{
			genericMenu.AddItem(new GUIContent("Show/" + label), selectedLabels.Contains(label), ToggleLabelCallback, label);
		}
	}

	private void GenerateSettingsMenu(GenericMenu settingsMenu)
	{
		settingsMenu.AddItem(new GUIContent("Show Settings"), settingsOpen, () => { settingsOpen = !settingsOpen; });
		settingsMenu.AddSeparator("");
		settingsMenu.AddItem(new GUIContent("Select In Inspector", "Select prefabs in the inspector as well. This will show the inspector for the selected prefabs"), settings.selectInInspector, () => { settings.selectInInspector = !settings.selectInInspector; });
		settingsMenu.AddItem(new GUIContent("Multi Selection", "Allow for multiple selection"), settings.multiSelection, () => { settings.multiSelection = !settings.multiSelection; });
	}

	private void GenerateLabelContextMenu(GenericMenu menu,string label)
	{
		menu.AddItem(new GUIContent("Remove"), false, RemoveLabelCallback, label);
		menu.AddItem(new GUIContent("Remove All"), false, RemoveAllLabelsCallback);
	}
	#endregion

	#region Drawing

	#region Prefab List
	private void DrawPrefabList(Event current)
	{
		float elementWidth = settings.maxPrefabElementSize * settings.prefabElementSizeMultiply;
		float minSpacing = 8;
		int xCount = Mathf.FloorToInt((prefabListRect.width - minSpacing) / (elementWidth + minSpacing));
		float freeWidth = ((prefabListRect.width - minSpacing) - 12) - (xCount * elementWidth);
		float spacing = freeWidth / xCount;

		int yCount = Mathf.CeilToInt(prefabsList.Count / (float)xCount);

		contentRect = new Rect(prefabListRect.x, 0, prefabListRect.width - 24, yCount * (elementWidth + spacing + 24) + spacing * 2);

		//disable the scrolling when dragging prefabs
		if (current.type == EventType.DragUpdated && DragAndDrop.GetGenericData("Prefab") != null)
		{
			DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
			current.Use();
		}

		scroll = GUI.BeginScrollView(prefabListRect, scroll, contentRect);

		for (int row = 0; row < yCount; row++)
		{
			for (int x = 0; x < xCount; x++)
			{
				int i = row * xCount + x;

				if (i < prefabsList.Count)
				{
					Rect elementPreviewRect = new Rect(prefabListRect.x + (elementWidth + spacing) * x + 4, prefabListRect.y + (elementWidth + spacing + 20) * row, elementWidth, elementWidth);
					Rect fullElementRect = new Rect(elementPreviewRect.x, elementPreviewRect.y, elementPreviewRect.width, elementPreviewRect.height + 20);

					if (fullElementRect.y <= prefabListRect.height + scroll.y && fullElementRect.y + fullElementRect.height >= scroll.y)
					{
						string assetPath = prefabsList[i].Path;
						Object asset = AssetDatabase.LoadAssetAtPath(assetPath, typeof(Object));
						Texture2D preview = AssetPreview.GetAssetPreview(asset) ?? AssetPreview.GetMiniThumbnail(asset);
						if (asset != null)
						{
							//events
							if (fullElementRect.Contains(current.mousePosition))
							{
								if (current.type == EventType.MouseDown)
								{
									GUI.FocusControl(prefabsList[i].Guid);
									if (current.control)
									{
										AddToSelection(prefabsList[i].Guid);
									}
									else
									{
										SetSelected(prefabsList[i].Guid);
									}

									Repaint();

									if (current.button == 0)
									{
										DragAndDrop.PrepareStartDrag();
										DragAndDrop.StartDrag("PrefabDrag");
										DragAndDrop.SetGenericData("Prefab", SelectedPrefab);
										DragAndDrop.objectReferences = new Object[0];
									}

									current.Use();
								}
								else if (current.type == EventType.ContextClick)
								{
									GenericMenu contextMenu = new GenericMenu();
									GeneratePrefabContextMenu(contextMenu,prefabsList[i]);
									current.Use();
								}
							}

							//drawing
							bool selected = IsSelected(prefabsList[i].Guid);
							GUI.SetNextControlName(prefabsList[i].Guid);
							GUI.Box(elementPreviewRect, new GUIContent(preview), prefabElementStyle);
							EditorGUI.LabelField(new Rect(elementPreviewRect.x, elementPreviewRect.y + elementWidth + 3, elementWidth, 18), new GUIContent(asset.name), selected ? "AssetLabel" : EditorStyles.label);
							if (selected)
							{
								GUI.Box(new Rect(elementPreviewRect.x, elementPreviewRect.y, elementPreviewRect.width, elementPreviewRect.height - 2), GUIContent.none, "TL SelectionButton PreDropGlow");
								if (settings.prefabElementSizeMultiply >= 1f)
								{
									string[] assetLabels = AssetDatabase.GetLabels(asset);
									for (int l = 0; l < assetLabels.Length; l++)
									{
										float minWidth, maxWidth;
										GUIStyle style = "AssetLabel Partial";
										style.CalcMinMaxWidth(new GUIContent(assetLabels[l]), out minWidth, out maxWidth);
										GUI.Label(new Rect(elementPreviewRect.x + elementPreviewRect.width - minWidth - 8, elementPreviewRect.y + elementPreviewRect.height - EditorGUIUtility.singleLineHeight - 8 - EditorGUIUtility.singleLineHeight * l, minWidth, EditorGUIUtility.singleLineHeight), new GUIContent(assetLabels[l]), style);
									}
								}
							}
						}
						else
						{
							GUI.Box(elementPreviewRect, new GUIContent("No Asset at: " + assetPath));
						}
					}
				}
			}
		}
		GUI.EndScrollView();
	}
	#endregion

	#region Side
	private void DrawSide(Event current)
	{
		GUILayout.BeginArea(sideRect);
		labelsScroll = EditorGUILayout.BeginScrollView(labelsScroll);
		GUILayout.Space(EditorGUIUtility.singleLineHeight);
		bool noLabels = selectedLabels == null || selectedLabels.Count <= 0;
		if (GUILayout.Button(new GUIContent("Show All"), noLabels ? "AssetLabel" : "AssetLabel Partial", GUILayout.Width(sideRect.width - EditorGUIUtility.singleLineHeight - 8)) && !noLabels)
		{
			selectedLabels.Clear();
			LoadPrefabInfos();
		}
		GUILayout.Space(4);
		foreach (var label in labels)
		{
			bool isSelected = selectedLabels.Contains(label);
			GUILayout.Label(new GUIContent(label), isSelected ? "AssetLabel" : "AssetLabel Partial", GUILayout.Width(sideRect.width - EditorGUIUtility.singleLineHeight - 8));
			Rect labelRect = GUILayoutUtility.GetLastRect();
			if (current.type == EventType.MouseDown && labelRect.Contains(current.mousePosition))
			{
				if (current.button == 0)
				{
					ToggleLabelCallback(label);
				}
				else if (current.button == 1)
				{
					GenericMenu labelContextMenu = new GenericMenu();
					GenerateLabelContextMenu(labelContextMenu,label);
					labelContextMenu.ShowAsContext();
				}
			}
			GUILayout.Space(4);
		}
		GUILayout.FlexibleSpace();
		EditorGUILayout.EndScrollView();
		if (GUILayout.Button(new GUIContent("Add"), "minibutton", GUILayout.Width(sideRect.width - EditorGUIUtility.singleLineHeight - 8)))
		{
			PopupWindow.Show(addLabelButtonRect, new AddLabelPopup(this));
		}
		if (current.type == EventType.Repaint) addLabelButtonRect = GUILayoutUtility.GetLastRect();
		GUILayout.Space(EditorGUIUtility.singleLineHeight);
		GUILayout.EndArea();
	}
	#endregion

	#region Toolbar
	private void DrawToolbar(Event current)
	{
		GUILayout.BeginArea(topToolbarRect, new GUIStyle("Toolbar"));
		EditorGUILayout.BeginHorizontal();
		//labels
		if (GUILayout.Button(new GUIContent("Labels"), "ToolbarDropDown"))
		{
			GenericMenu lablesMenu = new GenericMenu();
			GenerateLabelsMenu(lablesMenu);
			lablesMenu.ShowAsContext();
		}
		//replace
		GUI.enabled = Selection.GetFiltered(typeof (GameObject), SelectionMode.ExcludePrefab).Cast<GameObject>().Any();
		if (GUILayout.Button(new GUIContent("Replace", "Replace selected instance with the selected prefab"), "ToolbarButton"))
		{
			ReplaceWithPrefab.Replace();
		}
		GUI.enabled = true;
		//settings
		if (GUILayout.Button(new GUIContent("Settings"), "ToolbarDropDown"))
		{
			GenericMenu settingsMenu = new GenericMenu();
			GenerateSettingsMenu(settingsMenu);
			settingsMenu.ShowAsContext();
		}
		EditorGUILayout.Space();
		GUIContent content = EditorGUIUtility.IconContent(settings.pivot ? "ToolHandlePivot" : "ToolHandleCenter");
		content.text = settings.pivot ? "Pivot" : "Center";
		if (GUILayout.Button(content, "ToolbarButton", GUILayout.Width(70)))
		{
			settings.pivot = !settings.pivot;
		}
		content = EditorGUIUtility.IconContent("renderdoc");
		content.text = "Surface";
		content.tooltip = "Snap prefabs to the placing surface";
		settings.followSurface = GUILayout.Toggle(settings.followSurface, content, "ToolbarButton", GUILayout.Width(80));

		content = EditorGUIUtility.IconContent("Toolbar Plus");
		content.text = "Paint";
		content.tooltip = "Paint in the scene view with the selected prefab";
		settings.paint = GUILayout.Toggle(settings.paint, content, "ToolbarButton", GUILayout.Width(80));
		GUILayout.FlexibleSpace();
		EditorGUI.BeginChangeCheck();
		settings.prefabElementSizeMultiply = EditorGUILayout.Slider(settings.prefabElementSizeMultiply, 0.5f, 1f, GUILayout.Width(128));
		settings.search = SearchField(GUIContent.none, settings.search, GUILayout.Height(EditorGUIUtility.singleLineHeight));
		if (EditorGUI.EndChangeCheck())
		{
			LoadPrefabInfos();
		}
		EditorGUILayout.EndHorizontal();
		GUILayout.EndArea();
	}
	#endregion

	#region Footer
	private void DrawFooter(Event current)
	{
		GUILayout.BeginArea(footerRect, new GUIStyle("ProjectBrowserBottomBarBg"));
		GUILayout.Space(4);
		EditorGUILayout.BeginHorizontal();
		if (selectedGuids.Count > 0)
		{
			foreach (var selectedGuid in selectedGuids)
			{
				Object asset = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(selectedGuid), typeof (Object));
				if (asset == null) continue;
				EditorGUILayout.LabelField(new GUIContent(asset.name, AssetPreview.GetMiniThumbnail(asset)));
				if (selectedGuids.Count == 1)
				{
					GUILayout.FlexibleSpace();
					string[] assetLabels = AssetDatabase.GetLabels(asset);
					foreach (var assetLabel in assetLabels)
					{
						GUILayout.Label(new GUIContent(assetLabel), "AssetLabel");
					}
				}
			}
			if (selectedGuids.Count > 1)
			{
				GUILayout.FlexibleSpace();
			}
		}
		EditorGUILayout.Space();
		EditorGUILayout.EndHorizontal();
		GUILayout.EndArea();
	}
	#endregion

	#region Settings

	private void DrawSettings(Event current)
	{
		GUILayout.BeginArea(prefabListRect);
		EditorGUILayout.Space();
		EditorGUILayout.LabelField(new GUIContent("Settings"),new GUIStyle("ProjectBrowserHeaderBgTop"));
		EditorGUILayout.Space();
		EditorGUI.BeginChangeCheck();
		settings.selectInInspector = EditorGUILayout.Toggle(new GUIContent("Select In Inspector","Select the prefab object in the inspector as well"), settings.selectInInspector);
		settings.multiSelection = EditorGUILayout.Toggle(new GUIContent("Enable Multi-Selection","Enable multi-selection of prefabs"), settings.multiSelection);
		settings.prefabLabel = EditorGUILayout.TextField(new GUIContent("Prefab Label", "The Default prefab label required for prefabs to be show in the browser"), settings.prefabLabel);
		settings.maxPrefabElementSize = EditorGUILayout.FloatField(new GUIContent("Max Prefab Preview Size","The Maximum preview size of each prefab"), settings.maxPrefabElementSize);
		if (EditorGUI.EndChangeCheck())
		{
			SaveData();
		}
		EditorGUILayout.Space();
		EditorGUILayout.BeginHorizontal();
		if (GUILayout.Button(new GUIContent("Reset"), "minibuttonleft",GUILayout.Width(80)))
		{
			ResetSettings();
		}
		if (GUILayout.Button(new GUIContent("Exit"), "minibuttonmid", GUILayout.Width(80)))
		{
			settingsOpen = false;
		}
		if (GUILayout.Button(new GUIContent("Remove Labels"), "minibuttonright", GUILayout.Width(120)))
		{
			RemoveAllLabelsCallback();
		}
		
		EditorGUILayout.EndHorizontal();
		GUILayout.EndArea();
	}

	#endregion

	#endregion

	private void LoadStyles()
	{
		if (prefabElementStyle == null)
		{
			prefabElementStyle = new GUIStyle(GUI.skin.FindStyle("ShurikenModuleTitle"));
			prefabElementStyle.imagePosition = ImagePosition.ImageOnly;
			prefabElementStyle.margin = new RectOffset(0, 0, 0, 0);
			prefabElementStyle.padding = new RectOffset(0, 0, 0, 0);
			prefabElementStyle.clipping = TextClipping.Clip;
			prefabElementStyle.contentOffset = Vector2.zero;
			prefabElementStyle.padding = new RectOffset(4, 4, 4, 4);
			prefabElementStyle.alignment = TextAnchor.MiddleCenter;
			prefabElementStyle.fixedHeight = 0;
			prefabElementStyle.border = new RectOffset(4, 4, 4, 4);
		}
	}

	#region Selection

	private void SetSelected(string guid)
	{
		selectedGuids.Clear();
		selectedGuids.Add(guid);
		if (settings.selectInInspector) Selection.activeObject = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), typeof (Object));
	}

	private void AddToSelection(string guid)
	{
		if (settings.multiSelection)
		{
			selectedGuids.Add(guid);
			Selection.objects = Add(Selection.objects, AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), typeof (Object)));
		}
		else
		{
			SetSelected(guid);
		}
	}

	private bool IsSelected(string guid)
	{
		return selectedGuids.Contains(guid);

	}

	#endregion

	private void LoadData()
	{
		const string prefix = "PrefabBrowser_";

		selectedGuids = new HashSet<string>(EditorPrefs.GetString(prefix + "SelectedGuids").Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries).Select(l => FirstLetterToUpper(l)));
		scroll.y = EditorPrefs.GetFloat(prefix + "ScrollY");
		labels = EditorPrefs.GetString(prefix + "LabelsList").Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries).ToList();
		labels.Sort();
		selectedLabels = new HashSet<string>(EditorPrefs.GetString(prefix + "SelectedLabels").Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries).Select(l => FirstLetterToUpper(l)));

		//settings
		if (EditorPrefs.HasKey(prefix + "SearchFilter")) settings.search = EditorPrefs.GetString(prefix + "SearchFilter");
		if (EditorPrefs.HasKey(prefix + "SelectInInspector")) settings.selectInInspector = EditorPrefs.GetBool(prefix + "SelectInInspector");
		if (EditorPrefs.HasKey(prefix + "MultiSelection")) settings.multiSelection = EditorPrefs.GetBool(prefix + "MultiSelection");
		if (EditorPrefs.HasKey(prefix + "Pivot")) settings.pivot = EditorPrefs.GetBool(prefix + "Pivot");
		if (EditorPrefs.HasKey(prefix + "Paint")) settings.paint = EditorPrefs.GetBool(prefix + "Paint");
		if (EditorPrefs.HasKey(prefix + "FollowSurface")) settings.followSurface = EditorPrefs.GetBool(prefix + "FollowSurface");
		if (EditorPrefs.HasKey(prefix + "PrefabLabel")) settings.prefabLabel = EditorPrefs.GetString(prefix + "PrefabLabel");
		if (EditorPrefs.HasKey(prefix + "MaxPreviewSize")) settings.maxPrefabElementSize = EditorPrefs.GetFloat(prefix + "MaxPreviewSize");
		if (EditorPrefs.HasKey(prefix + "PreviewSizeMultiply")) settings.prefabElementSizeMultiply = EditorPrefs.GetFloat(prefix + "PreviewSizeMultiply");
	}

	private void SaveData()
	{
		const string prefix = "PrefabBrowser_";

		EditorPrefs.SetString(prefix + "SelectedGuids", string.Join(",", selectedGuids.ToArray()));
		EditorPrefs.SetFloat(prefix + "ScrollY", scroll.y);
		EditorPrefs.SetString(prefix + "LabelsList", string.Join(",", labels.ToArray()));
		EditorPrefs.SetString(prefix + "SelectedLabels", string.Join(",", selectedLabels.ToArray()));

		//settings
		EditorPrefs.SetString(prefix + "SearchFilter", settings.search);
		EditorPrefs.SetBool(prefix + "SelectInInspector", settings.selectInInspector);
		EditorPrefs.SetBool(prefix + "MultiSelection", settings.multiSelection);
		EditorPrefs.SetBool(prefix + "Pivot", settings.pivot);
		EditorPrefs.SetBool(prefix + "Paint", settings.paint);
		EditorPrefs.SetBool(prefix + "FollowSurface", settings.followSurface);
		EditorPrefs.SetString(prefix + "PrefabLabel",settings.prefabLabel);
		EditorPrefs.SetFloat(prefix + "MaxPreviewSize",settings.maxPrefabElementSize);
		EditorPrefs.SetFloat(prefix + "PreviewSizeMultiply", settings.prefabElementSizeMultiply);
	}

	private void ResetSettings()
	{
		const string prefix = "PrefabBrowser_";
		EditorPrefs.DeleteKey(prefix + "SelectInInspector");
		EditorPrefs.DeleteKey(prefix + "MultiSelection");
		EditorPrefs.DeleteKey(prefix + "Pivot");
		EditorPrefs.DeleteKey(prefix + "Paint");
		EditorPrefs.DeleteKey(prefix + "FollowSurface");
		EditorPrefs.DeleteKey(prefix + "PrefabLabel");
		EditorPrefs.DeleteKey(prefix + "MaxPreviewSize");
		EditorPrefs.DeleteKey(prefix + "SearchFilter");
		EditorPrefs.DeleteKey(prefix + "SearchFilter");

		settings = new Settings();
	}

	private void LoadPrefabInfos()
	{
		prefabsList.Clear();
		string filter = string.Format("{0} l:{1} t:{2}", settings.search, settings.prefabLabel, PrefabType);
		string[] prefabsGuids = AssetDatabase.FindAssets(filter);
		foreach (var guid in prefabsGuids)
		{
			string assetPath = AssetDatabase.GUIDToAssetPath(guid);
			Object asset = AssetDatabase.LoadAssetAtPath(assetPath, typeof (Object));
			string[] assetLables = AssetDatabase.GetLabels(asset);
			bool hasAllLables = selectedLabels.Count <= 0 || selectedLabels.All(selectedLabel => assetLables.Contains(selectedLabel));
			if (!hasAllLables) continue;
			prefabsList.Add(new PrefabInfo(guid, assetPath));
		}
		Repaint();
	}

	#region Utiliy

	public static string SearchField(GUIContent content, string search, params GUILayoutOption[] options)
	{
		EditorGUILayout.BeginHorizontal();
		search = EditorGUILayout.TextField(content, search, "ToolbarSeachTextField", options);
		if (GUILayout.Button(GUIContent.none, string.IsNullOrEmpty(search) ? "ToolbarSeachCancelButtonEmpty" : "ToolbarSeachCancelButton"))
		{
			search = string.Empty;
			GUI.FocusControl("");
		}
		EditorGUILayout.EndHorizontal();
		return search;
	}

	public static string FirstLetterToUpper(string str)
	{
		if (str == null)
			return null;

		if (str.Length > 1)
			return char.ToUpper(str[0]) + str.Substring(1);

		return str.ToUpper();
	}

	public static T[] Add<T>(T[] target, T item)
	{
		if (target == null)
		{
			//TODO: Return null or throw ArgumentNullException;
		}
		T[] result = new T[target.Length + 1];
		target.CopyTo(result, 0);
		result[target.Length] = item;
		return result;
	}

	public static T[] Push<T>(T[] target, T item)
	{
		T[] result = new T[target.Length + 1];
		target.CopyTo(result, 1);
		result[0] = item;
		return result;
	}

	public static Bounds GetRendererBounds(GameObject gameObject)
	{
		Bounds? bounds = null;
		Renderer[] renderers = gameObject.GetComponentsInChildren<Renderer>();
		foreach (var renderer in renderers)
		{
			if (!renderer.enabled) continue;
			if (!bounds.HasValue)
			{
				bounds = renderer.bounds;
			}
			else
			{
				bounds.Value.Encapsulate(renderer.bounds);
			}
		}
		return bounds ?? new Bounds(gameObject.transform.position, Vector3.zero);
	}

	#endregion

	#region Static Helpers

	public static GameObject SelectedPrefab
	{
		get
		{
			if (instance != null && instance.selectedGuids.Count > 0)
			{
				return AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(instance.selectedGuids.First()), typeof (Object)) as GameObject;
			}
			return null;
		}
	}

	#endregion

	private struct PrefabInfo
	{
		public string Guid;
		public string Path;

		public PrefabInfo(string guid, string path)
		{
			Guid = guid;
			Path = path;
		}
	}

	public class AddLabelPopup : PopupWindowContent
	{
		private Vector2 scroll;
		private string search = "";
		private Dictionary<string, float> allLabels;
		private PrefabBrowser prefabBrowser;
		private GUIStyle itemStyle;
		private bool selectSearchBar;

		public AddLabelPopup(PrefabBrowser prefabBrowser)
		{
			this.prefabBrowser = prefabBrowser;
		}

		public override Vector2 GetWindowSize()
		{
			return new Vector2(240, 300);
		}

		public override void OnOpen()
		{
			base.OnOpen();
			allLabels = (Dictionary<string, float>) typeof (AssetDatabase).GetMethod("GetAllLabels", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[0]);
			itemStyle = new GUIStyle("MenuItem");
			selectSearchBar = true;
		}

		public override void OnGUI(Rect rect)
		{
			EditorGUILayout.Space();
			GUI.SetNextControlName("Search");
			search = SearchField(GUIContent.none, search, GUILayout.Width(300));
			if (selectSearchBar)
			{
				selectSearchBar = false;
				GUI.FocusControl("Search");
			}
			scroll = EditorGUILayout.BeginScrollView(scroll);
			foreach (var label in allLabels)
			{
				if (!label.Key.Contains(search)) continue;
				bool isSelected = prefabBrowser.labels.Contains(FirstLetterToUpper(label.Key));
				Rect itemRect = GUILayoutUtility.GetRect(new GUIContent(label.Key), itemStyle);
				if (Event.current.type == EventType.Repaint)
				{
					itemStyle.Draw(itemRect, new GUIContent(FirstLetterToUpper(label.Key)), isSelected || itemRect.Contains(Event.current.mousePosition), isSelected, isSelected, false);
				}
				else if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
				{
					if (isSelected)
					{
						prefabBrowser.RemoveLabelCallback(FirstLetterToUpper(label.Key));
					}
					else
					{
						prefabBrowser.AddLabelCallback(FirstLetterToUpper(label.Key));
					}
				}
			}
			GUILayout.FlexibleSpace();
			EditorGUILayout.EndScrollView();

			if (Event.current.type == EventType.mouseMove)
			{
				editorWindow.Repaint();
			}
		}
	}
}