/* CachedGUI
   https://github.com/isonil/CachedGUI
   MIT
   Piotr 'ison' Walczak
*/

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CachedGUI
{

/// <summary>
/// Determines when a cached GUI region should be automatically marked as dirty and redrawn.
/// </summary>
public enum AutoDirtyMode
{
    Disabled, // for when there are no interactable elements
    Interaction,
    InteractionAndMouseMove,
    Hovering // for when there are hover animations
}

/// <summary>
/// Determines which non-repaint events can enter the region.
/// </summary>
public enum EventsFilter
{
    None, // for when there are no interactable elements
    AllExceptLayoutAndMouseMove,
    AllExceptLayout,
    All // for when you care about Layout or want to do the filtering yourself
}

public static class CachedGUI
{
    // types
    private struct CachedRegion
    {
        // identity
        public RenderTexture renderTexture;
        public Rect rect;
        public int ID;

        // dirtying
        public bool dirty;
        public int lastUsedFrame;
        public int lastFrameDirtiedFromMouse;
        public bool holdingMouseDown;
        public Vector2 lastKnownMousePos;

        // debug
        public int timesRedrawn;
        public int timesNonRepaintEventEntered;
    }

    // working vars
    private static List<CachedRegion> cachedRegions = new List<CachedRegion>();
    private static List<(CachedRegion, bool, int)> currentStack = new List<(CachedRegion, bool, int)>();
    private static List<Vector2> mousePosStack = new List<Vector2>();
    private static Vector2 repaintOffset;
    private static float uiScale = 1f; // last remembered UI scale
    private static float clearIfUIScaleChanges = 1f;
    private static int nextAutoAssignedID = 1;
    private static int autoCheckDestroyOldRegionsFrame;
    private static bool debugMode;

    // working vars - auto-dirtying
    private static Dictionary<(int, string), (object, int)> dirtyIfChanged = new Dictionary<(int, string), (object, int)>();
    private static Dictionary<(int, string), (int, int)> dirtyIfChanged_int = new Dictionary<(int, string), (int, int)>();
    private static Dictionary<(int, string), (float, int)> dirtyIfChanged_float = new Dictionary<(int, string), (float, int)>();
    private static Dictionary<(int, string), (bool, int)> dirtyIfChanged_bool = new Dictionary<(int, string), (bool, int)>();

    // properties
    public static Vector2 RepaintOffset => repaintOffset;
    public static bool DoingAnyRegionNow => currentStack.Count != 0;
    public static int? CurrentRegionID => DoingAnyRegionNow ? currentStack[currentStack.Count - 1].Item3 : null;
    public static bool DebugMode { get => debugMode; set => debugMode = value; }

    /// <summary>
    /// Begin new cached GUI region using sequential auto-assigned ID.
    /// </summary>
    public static bool Begin(Rect rect,
        ref int ID,
        AutoDirtyMode autoDirtyMode = AutoDirtyMode.InteractionAndMouseMove,
        EventsFilter eventsFilter = EventsFilter.AllExceptLayout,
        int autoDirtyEveryFrames = 150)
    {
        // first time - assign ID
        if( ID == 0 )
            ID = nextAutoAssignedID++;

        return Begin(rect, ID, autoDirtyMode, eventsFilter, autoDirtyEveryFrames);
    }

    /// <summary>
    /// Begin new cached GUI region using specific ID.
    /// </summary>
    public static bool Begin(Rect rect,
        int ID,
        AutoDirtyMode autoDirtyMode = AutoDirtyMode.InteractionAndMouseMove,
        EventsFilter eventsFilter = EventsFilter.AllExceptLayout,
        int autoDirtyEveryFrames = 150)
    {
        // get current UI scale from GUI.matrix
        uiScale = GUI.matrix.GetColumn(0).magnitude;

        // see if it's an event that dirties the region
        CheckAutoDirtyFromEvent(rect, ID, autoDirtyMode);
        
        if( Event.current.type != EventType.Repaint )
        {
            // we still need to add it, so End can remove it
            currentStack.Add((default, false, ID)); // CachedRegion doesn't matter here
            
            // determine if we want to skip this event
            if( eventsFilter == EventsFilter.None )
                return false; // skip
            else if( eventsFilter == EventsFilter.AllExceptLayoutAndMouseMove && (Event.current.type == EventType.Layout || Event.current.type == EventType.MouseMove || Event.current.type == EventType.MouseDrag) )
                return false; // skip
            else if( eventsFilter == EventsFilter.AllExceptLayout && Event.current.type == EventType.Layout )
                return false; // skip
            
            // update count for debug purposes
            int index = FindOrCreateRegion(rect, ID, canCreate: false);
            if( index != -1 )
            {
                var region = cachedRegions[index];
                region.timesNonRepaintEventEntered++;
                cachedRegions[index] = region;
            }

            return true; // enter GUI
        }
        else
        {
            // determine if we want to repaint

            // find existing entry
            int index = FindOrCreateRegion(rect, ID);
            var region = cachedRegions[index];
            bool wasDirty = region.dirty;

            // no longer mouseover
            if( region.lastFrameDirtiedFromMouse >= Time.frameCount - 2 )
                wasDirty = true;
            else if( autoDirtyEveryFrames >= 1
                && (Time.frameCount + Math.Abs(ID % 987)) % autoDirtyEveryFrames == 0 )
            {
                // dirty from time to time to avoid stale content
                wasDirty = true;
            }

            // rect size changed
            bool rectSizeChanged;
            if( rect.size != region.rect.size )
            {
                rectSizeChanged = true;
                int newNeededWidth = Mathf.CeilToInt(rect.width * uiScale);
                int newNeededHeight = Mathf.CeilToInt(rect.height * uiScale);
                int oldWidth = region.renderTexture.width;
                int oldHeight = region.renderTexture.height;

                // we need a bigger texture
                if( oldWidth < newNeededWidth || oldHeight < newNeededHeight )
                {
                    RenderTexture.Destroy(region.renderTexture);

                    // always enlarge by at least 10px
                    region.renderTexture = new RenderTexture(oldWidth < newNeededWidth ? Mathf.Max(newNeededWidth, oldWidth + 10) : oldWidth,
                        oldHeight < newNeededHeight ? Mathf.Max(newNeededHeight, oldHeight + 10) : oldHeight, 0);
                }
            }
            else
                rectSizeChanged = false;

            region.lastUsedFrame = Time.frameCount;
            region.rect = rect;
            region.dirty = false;
            cachedRegions[index] = region;

            if( rectSizeChanged || wasDirty )
            {
                region.timesRedrawn++;
                cachedRegions[index] = region;

                // redraw
                repaintOffset = -rect.position - GUIUtility.GUIToScreenPoint(Vector2.zero);
                BeginRenderTexture(region.renderTexture);
                currentStack.Add((region, true, region.ID));
                return true;
            }
            else
            {
                // we'll just use cache, no need to enter this GUI region
                currentStack.Add((region, false, region.ID));
                return false;
            }
        }
    }
    
    private static readonly GUIStyle DebugRectStyle = new GUIStyle { normal = new GUIStyleState { background = Texture2D.whiteTexture } };
    public static void End(float alpha = 1f)
    {
        if( !DoingAnyRegionNow )
        {
            Debug.LogError("Called CachedGUI.End() without matching CachedGUI.Begin().");
            return;
        }

        if( debugMode )
            alpha *= (Mathf.Sin(Time.unscaledTime * 8f) + 1f) / 2f * 0.7f + 0.2f;

        var elem = currentStack[currentStack.Count - 1];
        currentStack.RemoveAt(currentStack.Count - 1);

        bool usedRenderTexture = elem.Item2;
        var region = elem.Item1;

        // end render texture if used
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

            // debug background
            if( debugMode )
            {
                var oldCol = GUI.backgroundColor;
                GUI.backgroundColor = Color.red;
                GUI.Box(region.rect, GUIContent.none, DebugRectStyle);
                GUI.backgroundColor = oldCol;
            }

            // draw result
            var rect = new Rect(region.rect.x,
                region.rect.y,
                region.renderTexture.width / uiScale,
                region.renderTexture.height / uiScale);

            if( alpha < 1f )
                GUI.color = new Color(1f, 1f, 1f, alpha);

            GUI.DrawTexture(rect, region.renderTexture);
            
            if( alpha < 1f )
                GUI.color = Color.white;

            // debug content
            if( debugMode )
                GUI.Label(region.rect, "ID: " + region.ID + " Redrawn: " + region.timesRedrawn + " Events: " + region.timesNonRepaintEventEntered);
        }

        // in case someone doesn't call OnGUI() each frame
        if( Time.frameCount % 7 == 1 && autoCheckDestroyOldRegionsFrame != Time.frameCount )
        {
            autoCheckDestroyOldRegionsFrame = Time.frameCount;
            CheckDestroyOldRegions();
            CheckRemoveOldDirtyIfChanged();
        }
    }

    /// <summary>
    /// Marks this region for redraw.
    /// </summary>
    public static void SetDirty(int ID)
    {
        for( int i = 0, count = cachedRegions.Count; i < count; i++ )
        {
            if( cachedRegions[i].ID == ID )
            {
                var region = cachedRegions[i];
                region.dirty = true;
                cachedRegions[i] = region;
                return;
            }
        }
    }

    /// <summary>
    /// Marks all regions as dirty.
    /// </summary>
    public static void SetAllDirty()
    {
        for( int i = 0, count = cachedRegions.Count; i < count; i++ )
        {
            var region = cachedRegions[i];
            region.dirty = true;
            cachedRegions[i] = region;
        }
    }

    /// <summary>
    /// Destroys all render textures.
    /// </summary>
    public static void Clear()
    {
        for( int i = cachedRegions.Count - 1; i >= 0; i-- )
        {
            RenderTexture.Destroy(cachedRegions[i].renderTexture);
            cachedRegions.RemoveAt(i);
        }
    }

    /// <summary>
    /// Must be called each frame.
    /// </summary>
    public static void OnGUI()
    {
        // error recovery
        if( currentStack.Count != 0 )
        {
            Debug.LogError("CachedGUI stack is not empty. Clearing.");
            currentStack.Clear();
            RenderTexture.active = null;
        }

        if( mousePosStack.Count != 0 )
        {
            Debug.LogError("CachedGUI mouse position stack is not empty. Clearing.");
            mousePosStack.Clear();
        }

        CheckDestroyOldRegions();
        CheckRemoveOldDirtyIfChanged();

        // clear if UI scale changed
        if( Event.current.type == EventType.Repaint )
        {
            // get current UI scale from GUI.matrix
            float scale = GUI.matrix.GetColumn(0).magnitude;

            if( Mathf.Abs(scale - clearIfUIScaleChanges) > 0.002f )
            {
                clearIfUIScaleChanges = scale;
                Clear();
            }
        }
    }

    /// <summary>
    /// Sets currently drawn region as dirty.
    /// </summary>
    public static void DirtyCurrent()
    {
        if( currentStack.Count == 0 )
            return;

        SetDirty(currentStack[currentStack.Count - 1].Item3);
    }

    /// <summary>
    /// Must be called after every GUI.EndGroup() and alike.
    /// </summary>
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
        if( currentStack.Count == 0 )
            return;

        DirtyIfChanged(currentStack[currentStack.Count - 1].Item3, obj, name);
    }

    // overloads to avoid boxing
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
        if( currentStack.Count == 0 )
            return;

        DirtyIfChanged(currentStack[currentStack.Count - 1].Item3, value, name);
    }

    // overloads to avoid boxing
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
        if( currentStack.Count == 0 )
            return;

        DirtyIfChanged(currentStack[currentStack.Count - 1].Item3, value, name);
    }

    // overloads to avoid boxing
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
        if( currentStack.Count == 0 )
            return;

        DirtyIfChanged(currentStack[currentStack.Count - 1].Item3, value, name);
    }

    private static int FindOrCreateRegion(Rect rect, int ID, bool canCreate = true)
    {
        for( int i = 0, count = cachedRegions.Count; i < count; i++ )
        {
            if( cachedRegions[i].ID == ID )
                return i;
        }

        if( canCreate )
        {
            // add new entry
            cachedRegions.Add(new CachedRegion { rect = rect,
                lastUsedFrame = Time.frameCount,
                ID = ID,
                renderTexture = new RenderTexture(Mathf.CeilToInt(rect.width * uiScale), Mathf.CeilToInt(rect.height * uiScale), 0),
                dirty = true,
                lastFrameDirtiedFromMouse = -1 });

            return cachedRegions.Count - 1;
        }
        else
            return -1;
    }

    private static void CheckAutoDirtyFromEvent(Rect rect, int ID, AutoDirtyMode autoDirtyMode)
    {
        if( autoDirtyMode == AutoDirtyMode.Disabled )
            return;

        // if held mouse button inside and no longer holding (even if no longer inside the rect)
        if( Event.current.type == EventType.MouseUp )
        {
            for( int i = 0, count = cachedRegions.Count; i < count; i++ )
            {
                if( cachedRegions[i].ID == ID )
                {
                    if( cachedRegions[i].holdingMouseDown )
                    {
                        var region = cachedRegions[i];
                        region.holdingMouseDown = false;
                        region.dirty = true;
                        cachedRegions[i] = region;
                        return;
                    }

                    break;
                }
            }
        }
        // if held mouse button inside and there's a mouse drag event (even if no longer inside the rect)
        if( (autoDirtyMode == AutoDirtyMode.InteractionAndMouseMove || autoDirtyMode == AutoDirtyMode.Hovering)
            && Event.current.type == EventType.MouseDrag )
        {
            for( int i = 0, count = cachedRegions.Count; i < count; i++ )
            {
                if( cachedRegions[i].ID == ID )
                {
                    if( cachedRegions[i].holdingMouseDown )
                    {
                        var region = cachedRegions[i];
                        region.dirty = true;
                        cachedRegions[i] = region;
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
            for( int i = 0, count = cachedRegions.Count; i < count; i++ )
            {
                if( cachedRegions[i].ID == ID )
                {
                    var region = cachedRegions[i];
                    region.lastFrameDirtiedFromMouse = Time.frameCount;
                    region.dirty = true;
                    cachedRegions[i] = region;
                    return;
                }
            }
        }

        // interaction
        if( Event.current.type == EventType.ScrollWheel
            || Event.current.type == EventType.MouseDown
            || Event.current.type == EventType.MouseUp
            || ((Event.current.type == EventType.KeyDown || Event.current.type == EventType.KeyUp) && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.Escape)) )
        {
            if( Event.current.type == EventType.MouseDown )
            {
                // set holding mouse down flag
                for( int i = 0, count = cachedRegions.Count; i < count; i++ )
                {
                    if( cachedRegions[i].ID == ID )
                    {
                        var region = cachedRegions[i];
                        region.holdingMouseDown = true;
                        cachedRegions[i] = region;
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
            for( int i = 0, count = cachedRegions.Count; i < count; i++ )
            {
                if( cachedRegions[i].ID == ID )
                {
                    if( Event.current.type == EventType.MouseMove
                        || Event.current.type == EventType.MouseDrag
                        || ((Event.current.type == EventType.Repaint || Event.current.type == EventType.Layout) && Event.current.mousePosition != cachedRegions[i].lastKnownMousePos) )
                    {
                        var region = cachedRegions[i];
                        region.lastFrameDirtiedFromMouse = Time.frameCount;
                        region.dirty = true;
                        region.lastKnownMousePos = Event.current.mousePosition;
                        cachedRegions[i] = region;
                        return;
                    }

                    break;
                }
            }
        }
    }

    private static void CheckDestroyOldRegions()
    {
        for( int i = cachedRegions.Count - 1; i >= 0; i-- )
        {
            if( Time.frameCount - cachedRegions[i].lastUsedFrame > 60 )
            {
                RenderTexture.Destroy(cachedRegions[i].renderTexture);
                cachedRegions.RemoveAt(i);
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