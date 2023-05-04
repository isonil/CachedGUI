# Description

Use just 2 lines of code to make your immediate GUI in Unity run 100 times faster (*YMMV).

This works by caching everything that would normally be rendered to screen onto a RenderTexture and then reusing the same RenderTexture each frame. Everything works seamlesssly, so you don't have to do anything special to make it work. The only caveat is that you need to notify the system whenever the contents should be redrawn. Alternatively, you can use an appropriate flag to do automatic refresh whenever the mouse is hovering over the cached area.

# Example

```C#
if( CachedGUI.CachedGUI.BeginCachedGUI(cachedRect, 1) ) // note that this does NOT create a clipping GUI group
{
   ... all your usual GUI code like GUI.Button()
}
CachedGUI.CachedGUI.EndCachedGUI();
```

Then, if you want to recalculate the contents:

```C#
CachedGUI.CachedGUI.SetDirty(1);
```

Arguments explained:
```C#
public static bool BeginCachedGUI(Rect rect, // this is the cached area, it should be a bounding box around all the contents you want to cache for this group
    int ID, // ID of the cached group, for setting the dirty flag. You can also use the "ref int" overload to get the ID assigned automatically
    AutoDirtyMode autoDirtyMode = AutoDirtyMode.InteractionAndMouseMove, // Hovering - the group will be repainted whenever the mouse is hovering over the cached area, this basically turns off the entire caching as long as the mouse is hovering over the area. Not great for performance, but nice if you don't want to handle dirtying yourself. InteractionAndMouseMove - repaint on mouse move or any interaction (mouse clicks). Interaction - repaint on interactions. Disabled - no auto-dirtying, assumes you'll handle it yourself
    int autoDirtyEveryFrames = 120, // automatically set as dirty every X frames to avoid stale content if you forgot to dirty somewhere, pass -1 to disable
    bool skipAllEvents = false) // a tiny handy optimization, if true, then all other events except for Repaint will be discarded (only useful if you don't have any interactive elements inside the cached group)
```

To set currently drawn cached part as dirty (e.g. in MouseMove event on button hover), use:
```C#
CachedGUI.CachedGUI.SetCurrentDirty();
```

If you plan on using GUI.BeginGroup() and alike inside cached groups, then you have to call
```C#
CachedGUI.CachedGUI.SetCorrectMousePosition();
```
after every such call. Either that, or you have to handle mouse position manually by using CachedGUI.CachedGUI.RepaintOffset.

You can turn on debug mode and visualize all cached groups like so:
```C#
CachedGUI.CachedGUI.DebugMode = true;
```
