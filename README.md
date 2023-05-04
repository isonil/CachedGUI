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
   int ID, // ID of the cached group, for setting the dirty flag
   bool allowNonRepaintEvents = true, // a tiny handy optimization, if false, then all other events except for Repaint will be discarded (only useful if you don't have any interactive elements inside the cached group)
   bool dirtyOnMouseover = false, // if true, the group will be repainted whenever the mouse is hovering over the cached area, this basically turns off the entire caching as long as the mouse is hovering over the area. Not great for performance, but nice if you don't want to handle dirtying yourself.
   bool warnAboutSizeChange = true) // if true, will warn you if the same cached group has changed size, since it involves destroying and recreating the render texture
```
