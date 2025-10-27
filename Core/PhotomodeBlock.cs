namespace SprocketMultiplayer.Core {
    public class PhotomodeBlock {
        public static bool IsMultiplayer;

        public static bool CanUsePhotomode => !IsMultiplayer;
        
        
    }
}