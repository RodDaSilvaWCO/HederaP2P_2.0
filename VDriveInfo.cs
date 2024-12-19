using System.Text.Json.Serialization;

namespace WorldComputer.Simulator
{
    public class VDriveInfo
    {
        [JsonPropertyName("DL")]
        public char DriveLetter { get; set; }

        [JsonPropertyName("VDST")]
        public string VirtualDiskSessionToken { get; set; }

        [JsonPropertyName("VST")]
        public string VolumeSessionToken { get; set; }

        [JsonPropertyName("SS")]
        public int SectorSize { get; set; }

        [JsonPropertyName("BS")]
        public uint BlockSize { get; set; }

        [JsonPropertyName("MRBS")]
        public int MaxReadBlockSize { get; set; }

        [JsonPropertyName("MWBS")]
        public int MaxWriteBlockSize { get; set; }

        public VDriveInfo() { }  // parameterless constructor required for deserialization
    }
}
