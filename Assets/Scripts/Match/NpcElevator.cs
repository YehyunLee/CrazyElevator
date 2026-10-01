using UnityEngine;

namespace CrazyElevator.Match
{
    using Game = CrazyElevator.Managers.ElevatorManager;

    // A deliberately readable opponent: deliver, board, then pick a nearby stop.
    public sealed class NpcElevator : MonoBehaviour
    {
        public Game game;
        public float decisionDelay = .85f;
        float decisionClock, boardingClock;
        int partiesBoarded;
        bool wasAtStop;

        public void ResetDecisions()
        {
            decisionClock = 0;
            boardingClock = 0;
            partiesBoarded = 0;
            wasAtStop = false;
        }

        void Update()
        {
            if (game == null || game.Match == null || !game.Match.Running
                || game.Match.Paused || game.ShiftFinished) return;
            if (game.Traveling)
            {
                if (wasAtStop) ResetDecisions();
                wasAtStop = false;
                game.NpcDriveToStop();
                return;
            }
            if (!game.AtStop) return;
            if (!wasAtStop)
            {
                boardingClock = 0;
                decisionClock = 0;
                partiesBoarded = 0;
                wasAtStop = true;
            }

            boardingClock += Time.deltaTime;
            decisionClock += Time.deltaTime;
            if (decisionClock < decisionDelay || game.BusyWithPassenger) return;
            decisionClock = 0;
            if (game.NpcDeliver()) return;
            if (partiesBoarded < 3 && boardingClock < 3.5f && game.NpcBoard())
            { partiesBoarded++; return; }
            if (boardingClock >= 2.5f) game.NpcChooseFloor();
        }
    }
}
