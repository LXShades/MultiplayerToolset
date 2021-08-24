using Mirror;
using UnityEngine;


namespace MultiplayerToolset.Examples.Mirror
{
    public class TickerController : NetworkBehaviour
    {
        PhysicsTickable physTickable;

        Ticker<PhysicsTickable.Input, PhysicsTickable.State> physTicker;

        private float timeOnServer;
        private float timeOfLastServerUpdate;

        public float clientExtrapolation = 0.5f;

        public int updatesPerSecond = 30;

        private void Start()
        {
            physTickable = FindObjectOfType<PhysicsTickable>();
            physTicker = physTickable.GetTicker() as Ticker<PhysicsTickable.Input, PhysicsTickable.State>;
        }

        // Update is called once per frame
        void Update()
        {
            // seek physics
            if (NetworkServer.active)
                physTicker.Seek(Time.time, Time.time);
            else
                physTicker.Seek(timeOnServer + Time.time - timeOfLastServerUpdate + clientExtrapolation, timeOnServer, TickerSeekFlags.IgnoreDeltas);

            // send target ticker's state to clients
            if (NetworkServer.active && TimeTool.IsTick(Time.unscaledTime, Time.unscaledDeltaTime, updatesPerSecond))
                RpcState(physTicker.lastConfirmedState, physTicker.confirmedStateTime, Time.time - physTicker.confirmedStateTime);
        }

        [ClientRpc(channel = Channels.Unreliable)]
        private void RpcState(PhysicsTickable.State state, float time, float serverExtrapolation)
        {
            if (!NetworkServer.active)
            {
                physTicker.Reconcile(state, time, 0/*TickerSeekFlags.DontConfirm*/);
                timeOnServer = time + serverExtrapolation;
                timeOfLastServerUpdate = Time.time;
            }
        }
    }
}