/* CachedGUI
   https://github.com/isonil/CachedGUI
   MIT
   Piotr 'ison' Walczak
*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CachedGUI
{

public enum AutoDirtyMode
{
    Hovering,
    InteractionAndMouseMove,
    Interaction,
    Disabled
}

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
        public bool holdingMouseDown;
        public Vector2 lastKnownMousePos;
        public int timesRedrawn;
    }

    // working vars
    private static List<CachedPart> cachedParts = new List<CachedPart>();
    private static List<(CachedPart, bool, int)> stack = new List<(CachedPart, bool, int)>();
    private static List<Vector2> mousePosStack = new List<Vector2>();
    private static Vector2 repaintOffset;
    private static float uiScale = 1f;
    private static int nextAutoAssignedID;
    private static bool debugMode;
    private static Dictionary<(int, string), (object, int)> dirtyIfChanged = new Dictionary<(int, string), (object, int)>();
    private static Dictionary<(int, string), (int, int)> dirtyIfChanged_int = new Dictionary<(int, string), (int, int)>();
    private static Dictionary<(int, string), (float, int)> dirtyIfChanged_float = new Dictionary<(int, string), (float, int)>();
    private static Dictionary<(int, string), (bool, int)> dirtyIfChanged_bool = new Dictionary<(int, string), (bool, int)>();
    private static int autoCheckDestroyOldPartsFrame;

    // properties
    public static Vector2 RepaintOffset => repaintOffset;
    public static bool InAnyGroup => stack.Count != 0;
    public static int? CurrentGroupID => InAnyGroup ? (int?)stack[stack.Count - 1].Item3 : null;
    public static bool DebugMode { get => debugMode; set => debugMode = value; }

    public static bool BeginCachedGUI(Rect rect,
        ref int? ID,
        AutoDirtyMode autoDirtyMode = AutoDirtyMode.InteractionAndMouseMove,
        int autoDirtyEveryFrames = 120,
        bool skipAllEvents = false)
    {
        if( ID == null )
            ID = nextAutoAssignedID++;

        return BeginCachedGUI(rect, ID.Value, autoDirtyMode, autoDirtyEveryFrames, skipAllEvents);
    }

    public static bool BeginCachedGUI(Rect rect,
        int ID,
        AutoDirtyMode autoDirtyMode = AutoDirtyMode.InteractionAndMouseMove,
        int autoDirtyEveryFrames = 120,
        bool skipAllEvents = false)
    {
        // get UI scale from GUI.matrix
        uiScale = GUI.matrix.GetColumn(0).magnitude;

        CheckDirtyFromEvent(rect, ID, autoDirtyMode);
                
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

        // no longer mouseover
        if( part.lastFrameDirtiedFromMouse >= Time.frameCount - 2 )
            wasDirty = true;
        else if( autoDirtyEveryFrames >= 1
            && (Time.frameCount + ID % 987) % autoDirtyEveryFrames == 0 )
        {
            // dirty from time to time to avoid stale content
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
            part.timesRedrawn++;
            cachedParts[index] = part;

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
    
    private static readonly GUIStyle DebugRectStyle = new GUIStyle { normal = new GUIStyleState { background = Texture2D.whiteTexture } };
    public static void EndCachedGUI(float alpha = 1f)
    {
        if( debugMode )
            alpha *= (Mathf.Sin(Time.unscaledTime * 8f) + 1f) / 2f * 0.7f + 0.2f;

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

            if( debugMode )
            {
                var oldCol = GUI.backgroundColor;
                GUI.backgroundColor = Color.red;
                GUI.Box(part.rect, GUIContent.none, DebugRectStyle);
                GUI.backgroundColor = oldCol;
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

            if( debugMode )
                GUI.Label(part.rect, "ID: " + part.ID + " Redrawn: " + part.timesRedrawn);
        }

        // in case someone doesn't call OnGUI() each frame
        if( Time.frameCount % 3 == 1 && autoCheckDestroyOldPartsFrame != Time.frameCount )
        {
            autoCheckDestroyOldPartsFrame = Time.frameCount;
            CheckDestroyOldParts();
            CheckRemoveOldDirtyIfChanged();
        }
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

    public static void SetAllDirty()
    {
        for( int i = 0; i < cachedParts.Count; i++ )
        {
            var part = cachedParts[i];
            part.dirty = true;
            cachedParts[i] = part;
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
        CheckRemoveOldDirtyIfChanged();
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

    public static void DirtyIfChanged(int ID, object obj, string name)
    {
        if( dirtyIfChanged.TryGetValue((ID, name), out var prev) )
        {
            if( !object.Equals(obj, prev.Item1) )
                SetDirty(ID);
                
            dirtyIfChanged[(ID, name)] = (obj, Time.frameCount);
        }
        else
        {
            dirtyIfChanged.Add((ID, name), (obj, Time.frameCount));
            SetDirty(ID);
        }
    }
    
    public static void DirtyCurrentIfChanged(object obj, string name)
    {
        if( stack.Count == 0 )
            return;

        DirtyIfChanged(stack[stack.Count - 1].Item3, obj, name);
    }

    // to avoid boxing
    public static void DirtyIfChanged(int ID, int value, string name)
    {
        if( dirtyIfChanged_int.TryGetValue((ID, name), out var prev) )
        {
            if( value != prev.Item1 )
                SetDirty(ID);
                
            dirtyIfChanged_int[(ID, name)] = (value, Time.frameCount);
        }
        else
        {
            dirtyIfChanged_int.Add((ID, name), (value, Time.frameCount));
            SetDirty(ID);
        }
    }

    public static void DirtyCurrentIfChanged(int value, string name)
    {
        if( stack.Count == 0 )
            return;

        DirtyIfChanged(stack[stack.Count - 1].Item3, value, name);
    }

    // to avoid boxing
    public static void DirtyIfChanged(int ID, bool value, string name)
    {
        if( dirtyIfChanged_bool.TryGetValue((ID, name), out var prev) )
        {
            if( value != prev.Item1 )
                SetDirty(ID);
                
            dirtyIfChanged_bool[(ID, name)] = (value, Time.frameCount);
        }
        else
        {
            dirtyIfChanged_bool.Add((ID, name), (value, Time.frameCount));
            SetDirty(ID);
        }
    }

    public static void DirtyCurrentIfChanged(bool value, string name)
    {
        if( stack.Count == 0 )
            return;

        DirtyIfChanged(stack[stack.Count - 1].Item3, value, name);
    }

    // to avoid boxing
    public static void DirtyIfChanged(int ID, float value, string name)
    {
        if( dirtyIfChanged_float.TryGetValue((ID, name), out var prev) )
        {
            if( value != prev.Item1 )
                SetDirty(ID);
                
            dirtyIfChanged_float[(ID, name)] = (value, Time.frameCount);
        }
        else
        {
            dirtyIfChanged_float.Add((ID, name), (value, Time.frameCount));
            SetDirty(ID);
        }
    }

    public static void DirtyCurrentIfChanged(float value, string name)
    {
        if( stack.Count == 0 )
            return;

        DirtyIfChanged(stack[stack.Count - 1].Item3, value, name);
    }

    private static void CheckDirtyFromEvent(Rect rect, int ID, AutoDirtyMode autoDirtyMode)
    {
        if( autoDirtyMode == AutoDirtyMode.Disabled )
            return;

        // no longer holding mouse down (even if no longer inside the rect)
        if( Event.current.type == EventType.MouseUp )
        {
            for( int i = 0; i < cachedParts.Count; i++ )
            {
                if( cachedParts[i].ID == ID )
                {
                    if( cachedParts[i].holdingMouseDown )
                    {
                        var part = cachedParts[i];
                        part.holdingMouseDown = false;
                        part.dirty = true;
                        cachedParts[i] = part;
                        return;
                    }

                    break;
                }
            }
        }

        var mousePos = Input.mousePosition;
        mousePos.y = Screen.height - mousePos.y;
        if( !rect.Contains(Event.current.mousePosition) && !rect.Contains(GUIUtility.ScreenToGUIPoint(mousePos)) )
            return;

        // hover
        if( autoDirtyMode == AutoDirtyMode.Hovering )
        {
            for( int i = 0; i < cachedParts.Count; i++ )
            {
                if( cachedParts[i].ID == ID )
                {
                    var part = cachedParts[i];
                    part.lastFrameDirtiedFromMouse = Time.frameCount;
                    part.dirty = true;
                    cachedParts[i] = part;
                    return;
                }
            }
        }

        // interaction
        if( Event.current.type == EventType.ScrollWheel
            || Event.current.type == EventType.MouseDown
            || Event.current.type == EventType.MouseUp )
        {
            if( Event.current.type == EventType.MouseDown )
            {
                // set holding mouse down flag
                for( int i = 0; i < cachedParts.Count; i++ )
                {
                    if( cachedParts[i].ID == ID )
                    {
                        var part = cachedParts[i];
                        part.holdingMouseDown = true;
                        cachedParts[i] = part;
                        break;
                    }
                }
            }

            SetDirty(ID);
            return;
        }

        // mouse move
        if( autoDirtyMode == AutoDirtyMode.InteractionAndMouseMove )
        {
            for( int i = 0; i < cachedParts.Count; i++ )
            {
                if( cachedParts[i].ID == ID )
                {
                    if( Event.current.type == EventType.MouseMove
                        || Event.current.type == EventType.MouseDrag
                        || ((Event.current.type == EventType.Repaint || Event.current.type == EventType.Layout) && Event.current.mousePosition != cachedParts[i].lastKnownMousePos) )
                    {
                        var part = cachedParts[i];
                        part.lastFrameDirtiedFromMouse = Time.frameCount;
                        part.dirty = true;
                        part.lastKnownMousePos = Event.current.mousePosition;
                        cachedParts[i] = part;
                        return;
                    }

                    break;
                }
            }
        }
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
    
    private static List<(int, string)> tmpToRemove = new List<(int, string)>();
    private static void CheckRemoveOldDirtyIfChanged()
    {
        tmpToRemove.Clear();

        foreach( var elem in dirtyIfChanged )
        {
            int lastUsedFrame = elem.Value.Item2;

            if( Time.frameCount - lastUsedFrame > 60 )
                tmpToRemove.Add(elem.Key);
        }
        for( int i = 0; i < tmpToRemove.Count; i++ )
        {
            dirtyIfChanged.Remove(tmpToRemove[i]);
        }

        tmpToRemove.Clear();
        
        foreach( var elem in dirtyIfChanged_int )
        {
            int lastUsedFrame = elem.Value.Item2;

            if( Time.frameCount - lastUsedFrame > 60 )
                tmpToRemove.Add(elem.Key);
        }
        for( int i = 0; i < tmpToRemove.Count; i++ )
        {
            dirtyIfChanged_int.Remove(tmpToRemove[i]);
        }

        tmpToRemove.Clear();

        foreach( var elem in dirtyIfChanged_float )
        {
            int lastUsedFrame = elem.Value.Item2;

            if( Time.frameCount - lastUsedFrame > 60 )
                tmpToRemove.Add(elem.Key);
        }
        for( int i = 0; i < tmpToRemove.Count; i++ )
        {
            dirtyIfChanged_float.Remove(tmpToRemove[i]);
        }

        tmpToRemove.Clear();
        
        foreach( var elem in dirtyIfChanged_bool )
        {
            int lastUsedFrame = elem.Value.Item2;

            if( Time.frameCount - lastUsedFrame > 60 )
                tmpToRemove.Add(elem.Key);
        }
        for( int i = 0; i < tmpToRemove.Count; i++ )
        {
            dirtyIfChanged_bool.Remove(tmpToRemove[i]);
        }

        tmpToRemove.Clear();
    }

    private static void BeginRenderTexture(RenderTexture renderTexture)
    {
        if( renderTexture == null )
            return;

        if( Event.current.type == EventType.Repaint )
        {
            RenderTexture.active = renderTexture;
            GUI.matrix = Matrix4x4.TRS(new Vector3(repaintOffset.x, repaintOffset.y, 0f) * uiScale, Quaternion.identity, new Vector3(uiScale, uiScale, 1));
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
            GUI.matrix = Matrix4x4.TRS(new Vector3(repaintOffset.x, repaintOffset.y, 0f) * uiScale, Quaternion.identity, new Vector3(uiScale, uiScale, 1));
            if( mousePosStack.Count != 0 )
            {
                Event.current.mousePosition = mousePosStack[mousePosStack.Count - 1];
                mousePosStack.RemoveAt(mousePosStack.Count - 1);
            }
        }
    }
}

}