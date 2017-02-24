[![Donate](https://img.shields.io/badge/Donate-PayPal-green.svg)](https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=PPZJQDNZNH54Y)

# Content
* [Event Manager](#event-manager)

# Event Manager

A global event manager with weighted listener support and generic listeners.
The Event Manager creates a GameObject named _EventManager_ that is marked as DontDestoryOnLoad. That means the event manager will persist even on level change.

## Adding listeners
To add listeners call the `AddListiner` method. Listeners can also be of any type that implements the `Event` class.
The listener is then queued and invoked the next frame. This helps with assigning custom event handlers the same frame as adding the listeners.

Listeners can also have handle object. There object is responsible for checking if the event has gone out of scope (Destroyed).

## Invoking events
Events can be invoked by using the `InvokeEvent` method. That method can accept all types of events derived from the `Event` class. If a listener has subscribed to a base class of an event. For example, the `Event` class itself. All events that derive from that class will be called as well. In our example all events will trigger the listener that has subscribed with the base class `Event`, as there is no lower base class than `Event`.

## Custom Event Handlers
Custom event handles such as `WeightedEventHandler` can be  added using the `RegisterEvent` method. All previous listeners will be transferred to the new handler. A default handler is created if an event is invoked and has no handler assigned. That means that all custom event handlers must be registered before triggering the events, or in the same frame as invoked events.