
using ProyectoFinalPDI.AForge;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace ProyectoFinalPDI
{
    public enum GameState { Idle, Playing, PoseSuccess, GameOver }

    public class GameEngine
    {
        public int Score { get; private set; }
        public int Lives { get; private set; } = 3;
        public int Round { get; private set; }
        public GameState State { get; private set; } = GameState.Idle;
        public PoseSnapshot CurrentTarget { get; private set; }
        public double TimeLeft { get; private set; }
        public double LastAccuracy { get; private set; }

        private double _roundDuration = 6.0;
        private readonly Random _rng = new Random();
        private readonly List<PoseSnapshot> _pool = ReferencePoses.All;

        public void StartGame()
        {
            Score = 0; Lives = 3; Round = 0; _roundDuration = 6.0;
            State = GameState.Playing;
            NextRound();
        }

        public void NextRound()
        {
            Round++;
            _roundDuration = Math.Max(3.0, 6.5 - Round * 0.25);
            TimeLeft = _roundDuration;
            CurrentTarget = _pool[_rng.Next(_pool.Count)];
            State = GameState.Playing;
        }

        public double UpdateFrame(SkinDetector.BodyPoints pts, double dt)
        {
            if (State != GameState.Playing || !pts.IsValid) return LastAccuracy;

            double acc = ComputeAccuracy(pts);
            LastAccuracy = acc;

            if (acc >= 72.0)
            {
                Score += (int)(acc * (1 + Round * 0.12));
                State = GameState.PoseSuccess;
                return acc;
            }

            TimeLeft -= dt;
            if (TimeLeft <= 0)
            {
                Lives--;
                State = Lives <= 0 ? GameState.GameOver : GameState.Playing;
                if (State == GameState.Playing) NextRound();
            }
            return acc;
        }

        private double ComputeAccuracy(SkinDetector.BodyPoints pts)
        {
            // Mapear BodyPoints a los keypoints relevantes de la pose objetivo
            var detected = new Dictionary<string, (double X, double Y)>
            {
                { "nose",           (pts.Head.X,      pts.Head.Y)      },
                { "left_shoulder",  (pts.ShoulderL.X, pts.ShoulderL.Y) },
                { "right_shoulder", (pts.ShoulderR.X, pts.ShoulderR.Y) },
                { "left_wrist",     (pts.HandL.X,     pts.HandL.Y)     },
                { "right_wrist",    (pts.HandR.X,     pts.HandR.Y)     },
                { "left_hip",       (pts.HipCenter.X - 0.07, pts.HipCenter.Y) },
                { "right_hip",      (pts.HipCenter.X + 0.07, pts.HipCenter.Y) },
            };

            double total = 0; int count = 0;
            foreach (var kv in detected)
            {
                if (!CurrentTarget.Keypoints.ContainsKey(kv.Key)) continue;
                var (tx, ty) = CurrentTarget.Keypoints[kv.Key];
                var (dx, dy) = kv.Value;
                double dist = Math.Sqrt(Math.Pow(dx - tx, 2) + Math.Pow(dy - ty, 2));
                total += Math.Max(0, 1.0 - dist / 0.18);
                count++;
            }
            return count == 0 ? 0 : (total / count) * 100.0;
        }
    }
}