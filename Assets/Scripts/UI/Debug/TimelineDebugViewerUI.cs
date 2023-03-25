﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Displays a debug timeline for the targetTicker
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class TimelineDebugViewerUI : MonoBehaviour
{
    private struct EntityUI
    {
        public GameObject gameObject;
        public Timeline.EntityBase entity;
        public UnityEngine.UI.Text entityName;
        public TimelineGraphic timelineUI;
    }

    public Timeline target { get; set; }

    [Header("Display")]
    public TimelineGraphic graphic;
    public float displayPeriod = 5f;
    public float displayPeriodSmoothTime = 0.25f;
    public bool stabiliseView = false;

    private float targetDisplayPeriod;
    private float displayPeriodLerpProgress = 0f;

    [Header("Marker Appearances")]
    public bool showAuthTime = false;

    public Color32 playbackTimeColor = Color.green;
    public float playbackTimeThickness = 3f;

    public Color32 stateColor = Color.cyan;
    public float stateHeight = 1f;
    public float stateOffset = 0f;

    public Color32 inputColor = Color.yellow;
    public float inputHeight = 1f;
    public float inputOffset = -1f;

    public Color32 initialHopColor = Color.red;
    public Color32 fullTickHopColor = Color.green;
    public Color32 forwardTickHopColor = Color.blue;
    public Color32 fullForwardTickHopColor = Color.yellow;
    public Color32 partialReplayingTickHopColor = Color.white;

    [Header("Entities")]
    public Transform entityUIContainer;
    public GameObject entityUIPrefab;

    private readonly Dictionary<Timeline.EntityBase, EntityUI> activeEntityTimelines = new Dictionary<Timeline.EntityBase, EntityUI>();

    private void Start()
    {
        targetDisplayPeriod = displayPeriod;
    }

    // Update is called once per frame
    void LateUpdate()
    {
        if (target != null)
        {
            RefreshEntityUIList();

            displayPeriodLerpProgress = Mathf.Min(displayPeriodLerpProgress + Time.deltaTime / displayPeriodSmoothTime, 1f);

            double latestConfirmedStateTime = target.GetTimeOfLatestConfirmedState();
            if (stabiliseView)
            {
                graphic.timeEnd = target.playbackTime + displayPeriod * 0.1f; // little hardcoded margin for now
                graphic.timeStart = graphic.timeEnd - displayPeriod;
            }
            else
            {
                graphic.timeStart = Mathf.Lerp(
                    (int)(Math.Max(target.playbackTime, latestConfirmedStateTime) / displayPeriod) * displayPeriod,
                    (int)(Math.Max(target.playbackTime, latestConfirmedStateTime) / targetDisplayPeriod) * targetDisplayPeriod,
                    displayPeriodLerpProgress * 2);
                graphic.timeEnd = graphic.timeStart + Mathf.Lerp(displayPeriod, targetDisplayPeriod, displayPeriodLerpProgress * 2 - 1);
            }

            graphic.ClearDraw();

            // Draw ticks
            graphic.DrawTick(target.playbackTime, 1.5f, 0f, playbackTimeColor, playbackTimeThickness, "Plybck", 0);
            graphic.DrawTick(latestConfirmedStateTime, 1.5f, 0f, stateColor, 1f, "State", 1);

            foreach (Timeline.EntityBase entity in target.entities)
            {
                TimelineTrackBase inputTrack = entity.inputTrackBase;
                TimelineTrackBase stateTrack = entity.stateTrackBase;

                for (int i = 0, e = inputTrack.Count; i < e; i++)
                    graphic.DrawTick(inputTrack.TimeAt(i), inputHeight, inputOffset, inputColor);
                for (int i = 0, e = stateTrack.Count; i < e; i++)
                    graphic.DrawTick(stateTrack.TimeAt(i), stateHeight, stateOffset, stateColor);

                if (activeEntityTimelines.TryGetValue(entity, out EntityUI entityUI))
                {
                    entityUI.timelineUI.ClearDraw();
                    entityUI.timelineUI.timeStart = graphic.timeStart;
                    entityUI.timelineUI.timeEnd = graphic.timeEnd;

                    for (int i = 0, e = inputTrack.Count; i < e; i++)
                        entityUI.timelineUI.DrawTick(inputTrack.TimeAt(i), inputHeight, inputOffset, inputColor);
                    for (int i = 0, e = stateTrack.Count; i < e; i++)
                        entityUI.timelineUI.DrawTick(stateTrack.TimeAt(i), stateHeight, stateOffset, stateColor);

                    if (entityUI.entityName.text != entity.name)
                        entityUI.entityName.text = entity.name;
                }
            }

            // Draw sequence events
            foreach (var seekOp in target.lastSeekDebugSequence)
            {
                if (seekOp.type == SeekOp.Type.DetermineStartTime)
                    graphic.DrawHop(seekOp.doubleA, seekOp.doubleB, initialHopColor, 1f, 1f);
                else if (seekOp.type == SeekOp.Type.Tick)
                {
                    if ((seekOp.flags & SeekOp.Flags.IsFullTick) != 0 && (seekOp.flags & SeekOp.Flags.IsForwardTick) != 0)
                        graphic.DrawHop(seekOp.doubleA, seekOp.doubleB, fullForwardTickHopColor, 1f, 0.5f, 2);
                    else if ((seekOp.flags & SeekOp.Flags.IsFullTick) != 0)
                        graphic.DrawHop(seekOp.doubleA, seekOp.doubleB, fullTickHopColor, 1f, 0.5f);
                    else if ((seekOp.flags & SeekOp.Flags.IsForwardTick) != 0)
                        graphic.DrawHop(seekOp.doubleA, seekOp.doubleB, forwardTickHopColor, 1f, 0.5f);
                    else
                        graphic.DrawHop(seekOp.doubleA, seekOp.doubleB, partialReplayingTickHopColor, 1f, 0.5f);
                }
            }
        }
        else
        {
            graphic.ClearDraw();
        }
    }

    private HashSet<Timeline.EntityBase> tempEntitiesToDelete = new HashSet<Timeline.EntityBase>();

    private void RefreshEntityUIList()
    {
        tempEntitiesToDelete.Clear();

        // remove ones that don't exist now, or all of them if this is not a good time to render them
        bool shouldForceDelete = !entityUIContainer.gameObject.activeInHierarchy || target == null;
        foreach (KeyValuePair<Timeline.EntityBase, EntityUI> entityTimeline in activeEntityTimelines)
        {
            if (shouldForceDelete || !target.entities.Contains(entityTimeline.Value.entity))
            {
                tempEntitiesToDelete.Add(entityTimeline.Value.entity);
                Destroy(entityTimeline.Value.gameObject);
            }
        }

        foreach (var toDelete in tempEntitiesToDelete)
            activeEntityTimelines.Remove(toDelete);

        // add ones that exist in the timeline but not in the UI
        foreach (Timeline.EntityBase entity in target.entities)
        {
            if (!activeEntityTimelines.ContainsKey(entity))
            {
                var entityTimelineRoot = Instantiate(entityUIPrefab, entityUIContainer);
                activeEntityTimelines.Add(entity, new EntityUI()
                {
                    gameObject = entityTimelineRoot,
                    entityName = entityTimelineRoot.GetComponentInChildren<UnityEngine.UI.Text>(),
                    timelineUI = entityTimelineRoot.GetComponentInChildren<TimelineGraphic>(),
                    entity = entity,
                });
            }
        }
    }

    public void SetDisplayPeriod(float targetDisplayPeriod)
    {
        displayPeriod = Mathf.Lerp(displayPeriod, targetDisplayPeriod, displayPeriodLerpProgress / displayPeriodSmoothTime);
        this.targetDisplayPeriod = targetDisplayPeriod;
        displayPeriodLerpProgress = 0f;
    }

    private GUIStyle hoverBoxStyle
    {
        get
        {
            if (_hoverBoxStyle == null)
            {
                _hoverBoxStyle = new GUIStyle(GUI.skin.box);
                _hoverBoxStyle.alignment = TextAnchor.UpperLeft;
                _hoverBoxStyle.wordWrap = true;
            }
            return _hoverBoxStyle;
        }
    }
    private GUIStyle _hoverBoxStyle;

    private void OnGUI()
    {
        if (graphic.rectTransform.rect.Contains(graphic.rectTransform.InverseTransformPoint(Input.mousePosition)) && target != null)
        {
            double time = graphic.TimeAtScreenX(Input.mousePosition.x);
            string lastInputInfo = "TODO";// target.GetInputInfoAtTime(time);
            string lastStateInfo = "TODO";// target.GetStateInfoAtTime(time);

            string textToDisplay = $"Playback: {target.playbackTime.ToString("F2")}\nHovered: {time.ToString("F2")}s\nInput:\n{lastInputInfo}\nState:\n{lastStateInfo}";
            Vector2 size = new Vector2(400f, hoverBoxStyle.CalcHeight(new GUIContent(textToDisplay), 400f));
            Rect infoBoxRect = new Rect(new Vector3(Input.mousePosition.x, Screen.height - Input.mousePosition.y), size);

            if (infoBoxRect.xMax > Screen.width)
                infoBoxRect.x = Screen.width - infoBoxRect.width;
            if (infoBoxRect.yMax > Screen.height)
            {
                var ok = Screen.height - Input.mousePosition.y - infoBoxRect.height;
                infoBoxRect.y = ok;
            }
            if (infoBoxRect.y < 0)
                infoBoxRect.y = 0;
            if (infoBoxRect.x < 0)
                infoBoxRect.x = 0;

            GUI.Box(infoBoxRect, textToDisplay, hoverBoxStyle);
        }
    }
}
