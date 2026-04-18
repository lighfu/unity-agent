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

        // Entry kind:
        //   "profile"       — BOOTH product that ships MochiFitter conversion data
        //                     (either standalone profile package or avatar bundle with profiles).
        //                     This is the default for legacy entries.
        //   "avatar_native" — BOOTH avatar product whose seller declares MochiFitter
        //                     compatibility on the product page, without the profile itself
        //                     being hosted there. Users still need to obtain the profile
        //                     separately (often from a third-party shop).
        // Missing / empty is interpreted as "profile" for backward compatibility.
        public string type;
    }

    internal static class MochiFitterEntryType
    {
        public const string Profile = "profile";
        public const string AvatarNative = "avatar_native";

        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return Profile;
            return raw == AvatarNative ? AvatarNative : Profile;
        }
    }
}
