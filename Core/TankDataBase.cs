using System;
using System.Collections.Generic;
using System.IO;

namespace SprocketMultiplayer.Core {
    public static class TankDatabase {
        public static List<TankInfo> LoadTanks() {
            List<TankInfo> list = new List<TankInfo>();

            string basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "My Games/Sprocket/Factions/AllowedVehicles/Blueprints/Vehicles"
            );

            string bpDir = basePath;
            string imgDir = Path.Combine(basePath, "Profiles");

            if (!Directory.Exists(bpDir)) return list;

            foreach (string bp in Directory.GetFiles(bpDir, "*.blueprint"))
            {
                string name = Path.GetFileNameWithoutExtension(bp);
                string png = Path.Combine(imgDir, name + ".png");

                list.Add(new TankInfo
                {
                    Name = name,
                    BlueprintPath = bp,
                    ImagePath = File.Exists(png) ? png : null
                });
            }

            return list;
        }
    }

    public class TankInfo
    {
        public string Name;
        public string BlueprintPath;
        public string ImagePath;
    }

}