/* CachedGUI
   https://github.com/isonil/CachedGUI
   MIT
   Piotr 'ison' Walczak
*/

using System.Collections.Generic;
using UnityEngine;

namespace CachedGUI
{

public static class CachedGUI
{
    // types
    private struct CachedPart
    {
        public RenderTexture renderTexture;
        public Rect rect;
        public int ID;
        public int lastUsedFrame;
        public bool dirty;
        public int lastFrameDirtiedFromMouse;
    }

    // working vars
    private static List<CachedPart> cachedParts = new List<CachedPart>();
    private static List<(CachedPart, bool, int)> stack = new List<(CachedPart, bool, int)>();
    private static List<Vector2> mousePosStack = new List<Vector2>();
    private static Vector2 repaintOffset;
    private static float uiScale = 1f;

    // properties
    public static Vector2 RepaintOffset => repaintOffset;

    public static bool BeginCachedGUI(Rect rect,
        int ID,
        bool dirtyOnMouseover = false,
        bool skipAllEvents = false)
    {
        // get UI scale from GUI.matrix
        uiScale = GUI.matrix.GetColumn(0).magnitude;

        if( Event.current.type != EventType.Repaint ) // we only cache graphics
        {
            stack.Add((default, false, ID)); // CachedPart doesn't matter here
            
            if( skipAllEvents )
                return false;
                
            return true;
        }

        // find existing entry
        int index = -1;
        for( int i = 0; i < cachedParts.Count; i++ )
        {
            if( cachedParts[i].ID == ID )
            {
                index = i;
                break;
            }
        }
        if( index == -1 )
        {
            // add new entry
            cachedParts.Add(new CachedPart { rect = rect,
                lastUsedFrame = Time.frameCount,
                ID = ID,
                renderTexture = new RenderTexture(Mathf.CeilToInt(rect.width * uiScale), Mathf.CeilToInt(rect.height * uiScale), 0),
                dirty = true,
                lastFrameDirtiedFromMouse = -1 });

            index = cachedParts.Count - 1;
        }

        var part = cachedParts[index];
        bool wasDirty = part.dirty;

        if( dirtyOnMouseover )
        {
            var mousePos = Input.mousePosition;
            mousePos.y = Screen.height - mousePos.y;
            if( rect.Contains(Event.current.mousePosition) || rect.Contains(GUIUtility.ScreenToGUIPoint(mousePos)) )
            {
                wasDirty = true;
                part.lastFrameDirtiedFromMouse = Time.frameCount;
            }
            else if( part.lastFrameDirtiedFromMouse >= Time.frameCount - 2 ) // no longer mouseover
                wasDirty = true;
        }

        // rect size changed
        bool rectSizeChanged;
        if( rect.size != part.rect.size )
        {
            rectSizeChanged = true;
            int newNeededWidth = Mathf.CeilToInt(rect.width * uiScale);
            int newNeededHeight = Mathf.CeilToInt(rect.height * uiScale);
            int oldWidth = part.renderTexture.width;
            int oldHeight = part.renderTexture.height;

            // we need a bigger texture
            if( oldWidth < newNeededWidth || oldHeight < newNeededHeight )
            {
                RenderTexture.Destroy(part.renderTexture);

                // always enlarge by at least 10px
                part.renderTexture = new RenderTexture(oldWidth < newNeededWidth ? Mathf.Max(newNeededWidth, oldWidth + 10) : oldWidth,
                    oldHeight < newNeededHeight ? Mathf.Max(newNeededHeight, oldHeight + 10) : oldHeight, 0);
            }
        }
        else
            rectSizeChanged = false;

        part.lastUsedFrame = Time.frameCount;
        part.rect = rect;
        part.dirty = false;
        cachedParts[index] = part;

        if( rectSizeChanged || wasDirty )
        {
            // redraw
            repaintOffset = -rect.position - GUIUtility.GUIToScreenPoint(Vector2.zero);
            BeginRenderTexture(part.renderTexture);
            stack.Add((part, true, part.ID));
            return true;
        }
        else
        {
            // we'll just use cache
            stack.Add((part, false, part.ID));
            return false;
        }
    }
    
    public static void EndCachedGUI(float alpha = 1f)
    {
        var elem = stack[stack.Count - 1];
        stack.RemoveAt(stack.Count - 1);

        bool usedRenderTexture = elem.Item2;
        var part = elem.Item1;

        if( usedRenderTexture )
        {
            repaintOffset = Vector2.zero;
            EndRenderTexture();
        }
            
        if( Event.current.type == EventType.Repaint && alpha > 0f )
        {
            if( GUI.color != Color.white )
            {
                Debug.LogWarning("Drawing cached GUI render texture with non-white GUI.color. Resetting.");
                GUI.color = Color.white;
            }

            // draw result
            var rect = new Rect(part.rect.x,
                part.rect.y,
                part.renderTexture.width / uiScale,
                part.renderTexture.height / uiScale);

            if( alpha < 1f )
                GUI.color = new Color(1f, 1f, 1f, alpha);

            GUI.DrawTexture(rect, part.renderTexture);
            
            if( alpha < 1f )
                GUI.color = Color.white;
        }

        // in case someone doesn't call OnGUI() each frame
        if( Time.frameCount % 3 == 1 )
            CheckDestroyOldParts();
    }

    public static void SetDirty(int ID)
    {
        for( int i = 0; i < cachedParts.Count; i++ )
        {
            if( cachedParts[i].ID == ID )
            {
                var part = cachedParts[i];
                part.dirty = true;
                cachedParts[i] = part;
                return;
            }
        }
    }

    public static void Clear()
    {
        for( int i = cachedParts.Count - 1; i >= 0; i-- )
        {
            RenderTexture.Destroy(cachedParts[i].renderTexture);
            cachedParts.RemoveAt(i);
        }
    }

    public static void OnGUI()
    {
        if( stack.Count != 0 )
        {
            Debug.LogError("CachedGUI stack is not empty. Clearing.");
            stack.Clear();
            RenderTexture.active = null;
        }

        if( mousePosStack.Count != 0 )
        {
            Debug.LogError("CachedGUI mouse position stack is not empty. Clearing.");
            mousePosStack.Clear();
        }

        CheckDestroyOldParts();
    }

    public static void DirtyCurrent()
    {
        if( stack.Count == 0 )
            return;

        SetDirty(stack[stack.Count - 1].Item3);
    }

    // must be called after every GUI.EndGroup() and alike
    public static void SetCorrectMousePosition()
    {
        if( mousePosStack.Count == 0 )
            return;

        var pos = Input.mousePosition;
        pos.y = Screen.height - pos.y;
        Event.current.mousePosition = GUIUtility.ScreenToGUIPoint(pos) + repaintOffset;
    }

    private static void CheckDestroyOldParts()
    {
        for( int i = cachedParts.Count - 1; i >= 0; i-- )
        {
            if( Time.frameCount - cachedParts[i].lastUsedFrame > 60 )
            {
                RenderTexture.Destroy(cachedParts[i].renderTexture);
                cachedParts.RemoveAt(i);
            }
        }
    }
    
    private static void BeginRenderTexture(RenderTexture renderTexture)
    {
        if( renderTexture == null )
            return;

        if( Event.current.type == EventType.Repaint )
        {
            RenderTexture.active = renderTexture;
            GUI.matrix = Matrix4x4.TRS(new Vector3(repaintOffset.x, repaintOffset.y, 0f) * uiScale, Quaternion.identity, new Vector3(uiScale, uiScale, 1)); // for some reason setting RenderTexture resets GUI.matrix, so we need to reapply it
            mousePosStack.Add(Event.current.mousePosition);
            Event.current.mousePosition += repaintOffset; // compensate for new GUI.matrix, so the values are the same as before
            GL.Clear(false, true, new Color(0f, 0f, 0f, 0f));
        }
    }

    private static void EndRenderTexture()
    {
        if( Event.current.type == EventType.Repaint )
        {
            RenderTexture.active = null;
            GUI.matrix = Matrix4x4.TRS(new Vector3(repaintOffset.x, repaintOffset.y, 0f) * uiScale, Quaternion.identity, new Vector3(uiScale, uiScale, 1)); // for some reason setting RenderTexture resets GUI.matrix, so we need to reapply it
            if( mousePosStack.Count != 0 )
            {
                Event.current.mousePosition = mousePosStack[mousePosStack.Count - 1];
                mousePosStack.RemoveAt(mousePosStack.Count - 1);
            }
        }
    }
}

}