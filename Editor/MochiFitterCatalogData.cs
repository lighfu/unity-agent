using System;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor
{
    [Serializable]
    internal class MochiFitterCatalogRoot
    {
        public int version;
        public List<MochiFitterProfileEntry> profiles = new List<MochiFitterProfileEntry>();
    }

    [Serializable]
    internal class MochiFitterProfileEntry
    {
        public string avatar;
        public string price;
        public string convType; // "forward", "reverse", "both", "unknown"
        public string shop;
        public string boothId;
        public string thumbnailUrl; // BOOTH product thumbnail (fetched from API)
    }
}
