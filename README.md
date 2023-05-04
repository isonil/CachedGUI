# Description

Use just 2 lines of code to make your immediate GUI in Unity run 100 times faster (*YMMV).

This works by caching everything that would normally be rendered to screen onto a RenderTexture and then reusing the same RenderTexture each frame. Everything works seamlesssly, so you don't have to do anything special to make it work. The only caveat is that you need to notify the system whenever the contents should be redrawn. Alternatively, you can use an appropriate flag to do automatic refresh whenever the mouse is hovering over the cached area.

# Example

```
if( CachedGUI.CachedGUI.BeginCachedGUI(cachedRect, 1)) // note that this does NOT create a clipping GUI group
{
   ... all your usual GUI code like GUI.Button()
}
CachedGUI.CachedGUI.EndCachedGUI();
```
