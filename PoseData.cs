using System.Collections.Generic;
using System.Windows;

namespace ProyectoFinalPDI
{
    public class PoseKeypoint
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double X { get; set; }  
        public double Y { get; set; } 
        public double Confidence { get; set; }
    }

    public class PoseSnapshot
    {
        public string Name { get; set; }
        public Dictionary<string, (double X, double Y)> Keypoints { get; set; }
    }

    public static class ReferencePoses
    {
        // Pose: brazos abiertos en T
        public static PoseSnapshot ArmsOpen => new PoseSnapshot
        {
            Name = "Brazos Abiertos",
            Keypoints = new Dictionary<string, (double, double)>
            {
                { "nose",          (0.50, 0.12) },
                { "left_shoulder", (0.30, 0.30) },
                { "right_shoulder",(0.70, 0.30) },
                { "left_elbow",    (0.10, 0.30) },
                { "right_elbow",   (0.90, 0.30) },
                { "left_wrist",    (0.02, 0.30) },
                { "right_wrist",   (0.98, 0.30) },
                { "left_hip",      (0.38, 0.58) },
                { "right_hip",     (0.62, 0.58) },
            }
        };

        // Pose: manos arriba
        public static PoseSnapshot HandsUp => new PoseSnapshot
        {
            Name = "Manos Arriba",
            Keypoints = new Dictionary<string, (double, double)>
            {
                { "nose",          (0.50, 0.15) },
                { "left_shoulder", (0.35, 0.33) },
                { "right_shoulder",(0.65, 0.33) },
                { "left_elbow",    (0.28, 0.18) },
                { "right_elbow",   (0.72, 0.18) },
                { "left_wrist",    (0.22, 0.05) },
                { "right_wrist",   (0.78, 0.05) },
                { "left_hip",      (0.38, 0.60) },
                { "right_hip",     (0.62, 0.60) },
            }
        };

        // Pose: brazo izquierdo arriba
        public static PoseSnapshot LeftArmUp => new PoseSnapshot
        {
            Name = "Brazo Izquierdo Arriba",
            Keypoints = new Dictionary<string, (double, double)>
            {
                { "nose",          (0.50, 0.15) },
                { "left_shoulder", (0.35, 0.33) },
                { "right_shoulder",(0.65, 0.33) },
                { "left_elbow",    (0.30, 0.18) },
                { "right_elbow",   (0.72, 0.40) },
                { "left_wrist",    (0.26, 0.05) },
                { "right_wrist",   (0.75, 0.48) },
                { "left_hip",      (0.38, 0.60) },
                { "right_hip",     (0.62, 0.60) },
            }
        };

        public static List<PoseSnapshot> All => new List<PoseSnapshot>
        {
            ArmsOpen, HandsUp, LeftArmUp
        };
    }
}