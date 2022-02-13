using System;
using System.Text;
using UnityEngine;

public delegate void TickerEvent(TickInfo tickInfo);

[Flags]
public enum TickerSeekFlags
{
    None = 0,

    /// <summary>
    /// Specifies that inputs should not use deltas. Useful when the full input history is not known, meaning a delta may not necessarily be correct
    /// 
    /// For example, a client receives an input for T=0 and T=1. At T=1 they extrapoalte the state noting that Jump has been pressed since T=0.
    /// However, they are unaware that jump was actually first pressed at T=0.5, and the state they received for T=1 has already jumped
    /// On the server, T=1 had no jump delta as T=0.5 already did that. On the client, T=1 is thought to have the jump delta despite being untrue, leading to inconsistency.
    /// </summary>
    IgnoreDeltas = 1,

    /// <summary>
    /// Specifies that states should not be confirmed during the seek--the state is allowed to diverge from the input feed's deltas
    /// 
    /// This is slightly more efficient as the character doesn't need to be rewound or fast-forwarded or to have its states confirmed and stored
    /// </summary>
    DontConfirm = 2,

    /// <summary>
    /// Treat this as an internal replay.
    /// 
    /// Use when you e.g. want to modify and replay a piece of the timeline, but don't want sounds, visual effects etc to play. Because it's not playing in realtime, it's just internally replaying a new version of things that already happened.
    /// </summary>
    TreatAsReplay = 4,
};

public enum TickerMaxInputRateConstraint
{
    /// <summary>
    /// A new inputs is ignored if it shares the same quantised chunk of time (for example a fixed 0.05 interval from 0) as the last
    /// </summary>
    QuantisedTime,

    /// <summary>
    /// A new input is ignored if it is inserted in under 1/maxInputRate seconds since the last input was inserted.
    /// Works for many cases but may not work well with TimeTool.Quantize setups after long periods of time, as the floating points become fuzzy
    /// </summary>
    Flexible
}

[System.Serializable]
public struct TickerSettings
{
    [Header("Tick")]
    [Tooltip("The maximum delta time to pass to tickable components")]
    public float maxDeltaTime;
    [Tooltip("The maximum amount of iterations we can run while extrapolating")]
    public int maxSeekIterations;

    [Header("Input")]
    [Tooltip("The maximum input rate in hz. If <=0, the input rate is unlimited. This should be restricted sensibly so that clients do not send too many inputs and save CPU.")]
    public int maxInputRate;
    [Tooltip("Defines how the input rate will be constrained")]
    public TickerMaxInputRateConstraint maxInputRateConstraint;

    [Header("Reconciling")]
    [Tooltip("Whether to reconcile even if the server's confirmed state matched the local state at the time")]
    public bool alwaysReconcile;

    [Header("History")]
    [Tooltip("How long to keep input, state, etc history in seconds. Should be able to fit in a bit more ")]
    public float historyLength;

    [Header("Debug")]
    public bool debugLogReconciles;
    public bool debugLogSeekWarnings;

#if UNITY_EDITOR
    public float debugSelfReconcileDelay;
    public bool debugSelfReconcile;
    public bool debugDrawReconciles;
#else
    // just don't do these debug things in builds
    [NonSerialized]
    public float debugSelfReconcileDelay;
    [NonSerialized]
    public bool debugSelfReconcile;
    [NonSerialized]
    public bool debugDrawReconciles;
#endif

    public static TickerSettings Default = new TickerSettings()
    {
        maxDeltaTime = 0.03334f,
        maxSeekIterations = 15,
        maxInputRate = 60,
        maxInputRateConstraint = TickerMaxInputRateConstraint.QuantisedTime,
        alwaysReconcile = false,
        historyLength = 1f,
        debugLogReconciles = false,
        debugSelfReconcileDelay = 0.3f,
        debugDrawReconciles = false,
        debugSelfReconcile = false,
        debugLogSeekWarnings = false
    };
}

public struct TickInfo
{
    //public float deltaTime;
    /// <summary>
    /// The time of this tick. This is after deltaTime (so eg first frame with deltaTime=0.5, time is 0.5, much like Unity)
    /// </summary>
    public double time;

    /// <summary>
    /// Whether this tick is part of a confirmation. A "confirmation" happens when:
    /// * There is a known input both before and after the tick period (inclusive)
    /// * OR the target time from the known input exceeds maxDeltaTime (a confirmation happens in this case)
    /// * AND seekFlags does not contain TickerSeekFlags.DontConfirm.
    /// 
    /// Confirmations exist so that inputs can have variable delta times, and they make sure both client/server get the same deltas and results.
    /// 
    /// Confirmations may occur multiple times between a single pair of inputs if the gap between them exceeds maxDeltaTime.
    /// This is usually nothing to be worried about, it just means the deltas are split into smaller pieces.
    /// </summary>
    public bool isConfirming;

    /// <summary>
    /// Whether this tick is approaching a target time greater than the last Seek.
    /// This is confusing so here's an example of why it's needed:
    /// * A player is ticking at time=5
    /// * Player receives a state confirmation in the past at time 4.5. playbackTime has been set to 4.5.
    /// * Next frame, the player is ticking at time=5.5. However, playbackTime is still 4.5, and we don't want to replay sound and visual effects between 4.5 and 5.0.
    /// 
    /// In the above scenario, isReplaying will be true between 4.5 and 5.0, and false from 5.0 beyond
    /// tl;dr isReplaying = currentTime > destinationTimeAtPreviousSeek
    /// </summary>
    public bool isReplaying;

    /// <summary>
    /// A confirmation may hop back slightly in time if extrapolation is used, but the destination is still forward in time.
    /// This creates an awkward scenario where it's not technically a replay, but it is partially replaying, but treating it as a full replay might miss crucial effects.
    /// In those cases, try isConfirmingNew
    /// </summary>
    public bool isConfirmingForward => !isReplaying && isConfirming;

    public TickerSeekFlags seekFlags;

    public static TickInfo Default = new TickInfo()
    {
        isReplaying = false
    };
}

/// <summary>
/// A Ticker allows you to run "ticks" based on inputs, storing the resulting "states" along the way.
/// 
/// * You can scrub through an object's history using the Seek function. 
/// * You can overwrite a state in the object's history using ConfirmStateAt, and those changes will be propagated to later states using the recorded inputs.
/// * Recorded inputs and states are in the Timeline members (inputTimeline, stateTimeline) with time-data pairs.
/// * Time can be in any format you like, recommended to be based on seconds. Times are internally stored as a double to suit a practically infinite spectrum. Deltas, however, use floats for efficiency as they will typically be much smaller.
/// * See "settings" for a bunch of overrideables, such as limits on delta time and more.
/// </summary>
public class Ticker<TInput, TState> : ITickerBase, ITickerStateFunctions<TState>, ITickerInputFunctions<TInput>
    where TInput : ITickerInput<TInput> where TState : ITickerState<TState>
{
    /// <summary>
    /// The name of the target
    /// </summary>
    public string targetName => (target is Component targetAsComponent) ? targetAsComponent.gameObject.name : "N/A";

    /// <summary>
    /// The target tickable component, class or struct
    /// </summary>
    public ITickable<TInput, TState> target;

    public TickerSettings settings = TickerSettings.Default;

    /// <summary>
    /// Whether this ticker has an owning player's input
    /// </summary>
    public bool hasInput => inputTimeline.Count > 0;

    /// <summary>
    /// The latest known input in this ticker
    /// </summary>
    public TInput latestInput => inputTimeline.Latest;

    /// <summary>
    /// The current playback time, based on no specific point of reference, but is expected to use the same time format as input and event history
    /// </summary>
    public double playbackTime { get; private set; }

    /// <summary>
    /// The last time that was Seeked to. Similar to playbackTime, but does not change during ConfirmStateAt. This is needed for isForward Usually you shouldn't care about this
    /// </summary>
    public double lastSeekTargetTime { get; private set; }

    /// <summary>
    /// The most recent confirmed state
    /// </summary>
    public TState lastConfirmedState => stateTimeline.Count > 0 ? stateTimeline.Latest : default;

    /// <summary>
    /// The most recent confirmed state time - the non-extrapolated playback time of the last confirmed state
    /// </summary>
    public double lastConfirmedStateTime => stateTimeline.Count > 0 ? stateTimeline.LatestTime : -1f;

    /// <summary>
    /// Whether the ticker is temporarily paused. When paused, the Seek() function may run, but will always tick to the time it was originally
    /// </summary>
    public bool isDebugPaused { get; private set; }

    // Timelines!
    public readonly TimelineList<TInput> inputTimeline = new TimelineList<TInput>();

    public readonly TimelineList<TState> stateTimeline = new TimelineList<TState>();

    private readonly TimelineList<TickerEvent> eventTimeline = new TimelineList<TickerEvent>();

    // for our clueless friends in ITickerBase-land who don't know what types we're using. ;)
    public TimelineListBase inputTimelineBase => inputTimeline;
    public TimelineListBase stateTimelineBase => stateTimeline;

    private bool doesStateImplementDebug;

    public Ticker(ITickable<TInput, TState> target)
    {
        this.target = target;
        doesStateImplementDebug = typeof(ITickerStateDebug).IsAssignableFrom(typeof(TState));
        ConfirmCurrentState();
    }

    /// <summary>
    /// Inserts a single input into the input history. Old inputs at the same time will be replaced.
    /// </summary>
    public void InsertInput(TInput input, double time)
    {
        int closestPriorInputIndex = inputTimeline.ClosestIndexBefore(time);

        if (settings.maxInputRate <= 0 || closestPriorInputIndex == -1
            || (settings.maxInputRateConstraint == TickerMaxInputRateConstraint.Flexible && time * settings.maxInputRate - inputTimeline.TimeAt(closestPriorInputIndex) * settings.maxInputRate >= 0.999f)
            || (settings.maxInputRateConstraint == TickerMaxInputRateConstraint.QuantisedTime && TimeTool.Quantize(time, settings.maxInputRate) != TimeTool.Quantize(inputTimeline.TimeAt(closestPriorInputIndex), settings.maxInputRate)))
        {
            // Add current player input to input history
            inputTimeline.Set(time, input);
        }
    }

    /// <summary>
    /// Stores an input pack into input history. Old inputs at the same time will be replaced.
    /// </summary>
    public void InsertInputPack(TickerInputPack<TInput> inputPack)
    {
        for (int i = inputPack.inputs.Length - 1; i >= 0; i--)
            InsertInput(inputPack.inputs[i], inputPack.times[i]);
    }

    /// <summary>
    /// Makes an input pack from the input history (useful for sending multiple inputs to server)
    /// </summary>
    public TickerInputPack<TInput> MakeInputPack(float maxLength)
    {
        return TickerInputPack<TInput>.MakeFromHistory(inputTimeline, maxLength);
    }

    /// <summary>
    /// Seeks forward by the given deltaTime, if possible
    /// </summary>
    public void SeekBy(float deltaTime)
    {
        Seek(playbackTime + deltaTime);
    }

    /// <summary>
    /// Seeks to the given time. Ticks going forward beyond the latest confirmed state will call Tick on the target, with the closest available inputs.
    /// New states up to that point may be confirmed into the timeline, unless DontConfirm is used.
    ///  
    /// If something goes wrong and the result is inaccurate - such as the seek limit being reached - Seek is still guaranteed to set playbackTime to the targetTime to avoid locking the object.
    /// </summary>
    public void Seek(double targetTime, TickerSeekFlags flags = TickerSeekFlags.None)
    {
        double initialPlaybackTime = playbackTime;
        Debug.Assert(settings.maxDeltaTime > 0f);

        // in debug pause mode we never seek to other times
        if (isDebugPaused)
            targetTime = playbackTime;

        // See if we can rewind to relevant confirmed keyframe (should we call states keyframes? which one is the easiest to comprehend?)
        bool isRewinding = targetTime < playbackTime;
        bool canAttemptConfirmNextState = (flags & TickerSeekFlags.DontConfirm) == 0 && targetTime > playbackTime;

        if (isRewinding || canAttemptConfirmNextState)
        {
            // Try rewind. If canAttemptConfirmNextState, the closest earlier keyframe will happen to be the latest confirmed state
            int closestStateBeforeTargetTime = stateTimeline.ClosestIndexBefore(targetTime, 0f);

            if (closestStateBeforeTargetTime != -1)
            {
                target.ApplyState(stateTimeline[closestStateBeforeTargetTime]);
                playbackTime = stateTimeline.TimeAt(closestStateBeforeTargetTime);
            }
            else
            {
                if (settings.debugLogSeekWarnings)
                    Debug.LogWarning($"Ticker.Seek({initialPlaybackTime.ToString("F2")}->{targetTime.ToString("F2")}): reverse seek could not find earlier state to seek to. Using current state.");
            }
        }

        // Begin moving forward through the timeline
        try
        {
            int numIterations = 0;

            // Execute ticks, grabbing and consuming inputs if they are available, or using the latest inputs
            while (playbackTime < targetTime)
            {
                TInput input = default;
                int inputIndex = inputTimeline.ClosestIndexBeforeOrEarliest(playbackTime, 0.001f);
                bool canConfirmState = false;
                double confirmStateTime = 0f;

                // Decide the delta time to use
                double deltaTime = targetTime - playbackTime;
                if (deltaTime >= settings.maxDeltaTime)
                {
                    deltaTime = settings.maxDeltaTime;

                    canConfirmState = canAttemptConfirmNextState;
                    confirmStateTime = playbackTime + deltaTime;
                }

                if (inputIndex != -1)
                {
                    double inputTime = inputTimeline.TimeAt(inputIndex);

                    if (inputIndex > 0)
                    {
                        // if we can do a full previous->next input tick, we can "confirm" this state
                        // note that we may subdivide the confirmed ticks by settings.maxDeltaTime, so we check based on the current playbackTime rather than the previous input's time
                        double timeToNextInput = inputTimeline.TimeAt(inputIndex - 1) - playbackTime;

                        if (deltaTime >= timeToNextInput && canAttemptConfirmNextState)
                        {
                            // we can confirm the resulting state
                            deltaTime = timeToNextInput;

                            canConfirmState = true;
                            confirmStateTime = inputTimeline.TimeAt(inputIndex - 1);
                        }
                    }

                    // use a delta if it's the crossing the beginning part of the input, otherwise extrapolate without delta
                    if (inputIndex + 1 < inputTimeline.Count && playbackTime <= inputTime && playbackTime + deltaTime > inputTime && (flags & TickerSeekFlags.IgnoreDeltas) == 0)
                        input = inputTimeline[inputIndex].WithDeltas(inputTimeline[inputIndex + 1]);
                    else
                        input = inputTimeline[inputIndex].WithDeltas(inputTimeline[inputIndex]);
                }

                if (deltaTime > 0f)
                {
                    TickInfo tickInfo = new TickInfo()
                    {
                        time = playbackTime + deltaTime,
                        isReplaying = playbackTime + deltaTime <= lastSeekTargetTime || (flags & TickerSeekFlags.TreatAsReplay) != 0,
                        isConfirming = canConfirmState,
                        seekFlags = flags
                    };

                    // invoke events
                    for (int i = 0; i < eventTimeline.Count; i++)
                    {
                        if (eventTimeline.TimeAt(i) >= playbackTime && eventTimeline.TimeAt(i) < playbackTime + deltaTime)
                            eventTimeline[i]?.Invoke(tickInfo);
                    }

                    // run a tick
                    target.Tick((float)deltaTime, input, tickInfo);

                    playbackTime += deltaTime;

                    if (canConfirmState)
                    {
                        // direct assignment of target time reduces tiny floating point differences (these differences can accumulate _fast_) and reduce reconciles
                        playbackTime = confirmStateTime;

                        // save confirmed state into the timeline.
                        // reapply the confirmed state to self to ensure we get the same result (states can be compressed and decompressed with slightly different results, we need max consistency)
                        target.ApplyState(ConfirmCurrentState());
                    }
                }

                // Clamp iterations at the maximum to avoid freezes
                numIterations++;
                if (numIterations == settings.maxSeekIterations)
                {
                    if (settings.debugLogSeekWarnings)
                        Debug.LogWarning($"Ticker.Seek({initialPlaybackTime.ToString("F2")}->{targetTime.ToString("F2")}): Hit max {numIterations} iterations on {targetName}. T (Confirmed): {playbackTime.ToString("F2")} ({lastConfirmedStateTime})");

                    if (canAttemptConfirmNextState)
                    {
                        // we can't process everything, risking a lockup, so accept the time we're given and call it confirmed
                        playbackTime = targetTime;
                        ConfirmCurrentState();
                    }

                    break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }

        // even if something went wrong, we prefer to say we're at the target time
        playbackTime = targetTime;
        lastSeekTargetTime = targetTime;

        // Perform history cleanup
        CleanupHistory();
    }

    /// <summary>
    /// Rewinds to pastState and fast forwards to the present time, using recorded inputs if available
    /// </summary>
    public void Reconcile(TState pastState, double pastStateTime, TickerSeekFlags seekFlags)
    {
        double originalPlaybackTime = playbackTime;

        ConfirmStateAt(pastState, pastStateTime);
        Seek(originalPlaybackTime, seekFlags);
    }

    /// <summary>
    /// Applies a state into the history. If the state differs from the earlier state, playbackTime will be reverted and the next Seek will technically be a reconcile.
    /// 
    /// CAUTION: If the state differs from the original state at that time:
    /// * The new state is confirmed
    /// * All future confirmed states are removed (as they will likely be different when seeked again)
    /// * The current playback time is set to the time provided.
    /// </summary>
    public void ConfirmStateAt(TState state, double time)
    {
        int index = stateTimeline.IndexAt(time, 0.0001f);

        // if we have a state at that time which is already equal, we don't need to rewind or do anything! things are as they should be.
        if (settings.alwaysReconcile || index == -1 || !stateTimeline[index].Equals(state))
        {
            if (settings.debugLogReconciles)
            {
                Debug.Log(
                    $"Reconcile {targetName}:\n" +
                    $"Time: {time.ToString("F2")}\n" +
                    $"Index: {index}\n" +
                    $"Diffs: {(index != -1 ? PrintStructDifferences("recv", "old", state, stateTimeline[index]) : "N/A")}");
            }

            playbackTime = time;
            target.ApplyState(state);

            stateTimeline.Set(time, state);
            stateTimeline.TrimAfter(time);
        }
    }

    /// <summary>
    /// Confirms the current character state. Needed to teleport or otherwise influence movement (except where events are used)
    /// </summary>
    public TState ConfirmCurrentState()
    {
        TState state = target.MakeState();
        stateTimeline.Set(playbackTime, state);
        return state;
    }

    /// <summary>
    /// Enables or disables debug pause. When paused, the Seek() function may run, but will always tick to the time it was originally.
    /// </summary>
    public void SetDebugPaused(bool isDebugPaused)
    {
        this.isDebugPaused = isDebugPaused;
    }

    /// <summary>
    /// Draws the current character state
    /// </summary>
    public void DebugDrawCurrentState(Color colour)
    {
        if (doesStateImplementDebug)
            (target.MakeState() as ITickerStateDebug).DebugDraw(colour);
    }

    private string PrintStructDifferences<T>(string aName, string bName, T structureA, T structureB)
    {
        StringBuilder stringBuilder = new StringBuilder(512);

        foreach (var member in typeof(T).GetFields())
        {
            object aValue = member.GetValue(structureA);
            object bValue = member.GetValue(structureB);

            if (!aValue.Equals(bValue))
                stringBuilder.AppendLine($"[!=] {member.Name}: ({aName}) {aValue.ToString()} != ({bName}) {bValue.ToString()}");
            else
                stringBuilder.AppendLine($"[=] {member.Name}: {aValue.ToString()}");
        }

        return stringBuilder.ToString();
    }

    /// <summary>
    /// Calls an event for the current playback time
    /// </summary>
    public void CallEvent(TickerEvent eventToCall)
    {
        int currentIndex = eventTimeline.IndexAt(playbackTime, 0f);

        if (currentIndex != -1)
        {
            eventTimeline[currentIndex] += eventToCall;
        }
        else
        {
            eventTimeline.Set(playbackTime, eventToCall, 0f);
        }
    }

    /// <summary>
    /// Prunes old history that shouldn't be needed anymore
    /// </summary>
    private void CleanupHistory()
    {
        double trimTo = playbackTime - settings.historyLength;

        // we always want to have a confirmed state ready for us among other things, so we tend to preserve at least one item in each list as the "last known"
        inputTimeline.TrimBeforeExceptLatest(trimTo);
        eventTimeline.TrimBeforeExceptLatest(trimTo);
        stateTimeline.TrimBeforeExceptLatest(trimTo);
    }

    /// <summary>
    /// Clears all timeline history
    /// </summary>
    private void ClearHistory()
    {
        inputTimeline.Clear();
        eventTimeline.Clear();
        stateTimeline.Clear();
    }
}
