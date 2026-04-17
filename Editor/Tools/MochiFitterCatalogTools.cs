using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class MochiFitterCatalogTools
    {
        [AgentTool("List MochiFitter catalog entries. Optional filters: keyword (avatar/shop name), convType ('forward','reverse','both'), priceFilter ('free','paid'), entryType ('profile' = MochiFitter conversion profile products; 'avatar_native' = BOOTH avatars whose own product page declares MochiFitter compatibility). Returns BOOTH links.")]
        public static string ListMochiFitterCatalog(
            string keyword = "",
            string convType = "",
            string priceFilter = "",
            string entryType = "")
        {
            var catalog = MochiFitterCatalogWindow.LoadCatalogStatic();
            if (catalog == null || catalog.profiles.Count == 0)
                return "Error: MochiFitter catalog is empty or not found.";

            string kw = string.IsNullOrWhiteSpace(keyword) ? null : keyword.ToLower();
            string typeFilter = string.IsNullOrWhiteSpace(entryType) ? null : entryType.Trim();

            var filtered = catalog.profiles.Where(p =>
            {
                if (!string.IsNullOrEmpty(convType) && p.convType != convType)
                    return false;
                if (priceFilter == "free" && !IsFree(p.price))
                    return false;
                if (priceFilter == "paid" && IsFree(p.price))
                    return false;
                if (typeFilter != null && MochiFitterEntryType.Normalize(p.type) != typeFilter)
                    return false;
                if (kw != null)
                {
                    bool match = (p.avatar ?? "").ToLower().Contains(kw)
                              || (p.shop ?? "").ToLower().Contains(kw);
                    if (!match) return false;
                }
                return true;
            }).ToList();

            if (filtered.Count == 0)
                return "No profiles found matching the filter criteria.";

            int profileCount = filtered.Count(p => MochiFitterEntryType.Normalize(p.type) == MochiFitterEntryType.Profile);
            int nativeCount = filtered.Count - profileCount;

            var sb = new StringBuilder();
            sb.AppendLine($"=== MochiFitter Catalog ({filtered.Count}/{catalog.profiles.Count} entries: {profileCount} profiles, {nativeCount} avatar-native) ===");
            sb.AppendLine();

            foreach (var p in filtered)
            {
                string tag = MochiFitterEntryType.Normalize(p.type) == MochiFitterEntryType.AvatarNative
                    ? "Avatar"
                    : p.convType == "both" ? "Both" : p.convType == "forward" ? "Fwd" : p.convType == "reverse" ? "Rev" : "?";
                sb.AppendLine($"  [{tag}] {p.avatar}  ({p.price})");
                sb.AppendLine($"    Shop: {p.shop}");
                sb.AppendLine($"    BOOTH: https://booth.pm/ja/items/{p.boothId}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        [AgentTool("Check if a MochiFitter profile exists for a given avatar name. Searches the catalog by avatar name (fuzzy match). Returns matching profiles with BOOTH links, or suggests alternatives. Use this before attempting outfit retargeting to check if MochiFitter can help.")]
        public static string CheckMochiFitterSupport(string avatarName)
        {
            if (string.IsNullOrWhiteSpace(avatarName))
                return "Error: avatarName is required.";

            var catalog = MochiFitterCatalogWindow.LoadCatalogStatic();
            if (catalog == null || catalog.profiles.Count == 0)
                return "MochiFitter catalog is empty or not found.";

            string query = avatarName.Trim().ToLower();

            // Exact and fuzzy matches
            var exact = new List<MochiFitterProfileEntry>();
            var partial = new List<MochiFitterProfileEntry>();

            foreach (var p in catalog.profiles)
            {
                string av = (p.avatar ?? "").ToLower();
                if (av == query)
                    exact.Add(p);
                else if (av.Contains(query) || query.Contains(av))
                    partial.Add(p);
            }

            var sb = new StringBuilder();

            if (exact.Count > 0)
            {
                sb.AppendLine($"=== MochiFitter profile found for '{avatarName}' ===");
                foreach (var p in exact)
                    AppendProfileInfo(sb, p);
                if (partial.Count > 0)
                {
                    sb.AppendLine($"\n--- Related profiles ---");
                    foreach (var p in partial.Take(5))
                        AppendProfileInfo(sb, p);
                }
            }
            else if (partial.Count > 0)
            {
                sb.AppendLine($"=== No exact match for '{avatarName}', but similar profiles found ===");
                foreach (var p in partial.Take(10))
                    AppendProfileInfo(sb, p);
            }
            else
            {
                sb.AppendLine($"No MochiFitter profile found for '{avatarName}'.");
                sb.AppendLine($"Total catalog: {catalog.profiles.Count} profiles.");
                sb.AppendLine($"The user may need to purchase/download a profile from BOOTH, or this avatar may not have one available yet.");
            }

            return sb.ToString();
        }

        [AgentTool("Detect avatars in the current scene/project and check MochiFitter profile availability for each. Scans for VRCAvatarDescriptor components in the scene, then cross-references with the MochiFitter catalog. Use this to give the user an overview of which avatars can use MochiFitter.")]
        public static string ScanAvatarsForMochiFitter()
        {
            var catalog = MochiFitterCatalogWindow.LoadCatalogStatic();
            if (catalog == null || catalog.profiles.Count == 0)
                return "MochiFitter catalog is empty or not found.";

            // Find all avatar descriptors in scene
            var descriptors = Object.FindObjectsOfType<Component>()
                .Where(c => c.GetType().Name == "VRCAvatarDescriptor")
                .ToList();

            if (descriptors.Count == 0)
                return "No VRCAvatarDescriptor found in the current scene. Open a scene with avatars first.";

            var sb = new StringBuilder();
            sb.AppendLine($"=== MochiFitter Availability for Scene Avatars ===");
            sb.AppendLine($"Catalog: {catalog.profiles.Count} profiles | Scene avatars: {descriptors.Count}");
            sb.AppendLine();

            foreach (var desc in descriptors)
            {
                string avatarName = desc.gameObject.name;
                sb.AppendLine($"--- {avatarName} ---");

                // Search catalog (fuzzy)
                string query = avatarName.ToLower();
                var matches = catalog.profiles.Where(p =>
                {
                    string av = (p.avatar ?? "").ToLower();
                    return av.Contains(query) || query.Contains(av)
                        || LevenshteinClose(av, query);
                }).ToList();

                if (matches.Count > 0)
                {
                    sb.AppendLine($"  [FOUND] {matches.Count} matching profile(s):");
                    foreach (var m in matches.Take(3))
                    {
                        string tag = MochiFitterEntryType.Normalize(m.type) == MochiFitterEntryType.AvatarNative
                            ? "Avatar"
                            : m.convType == "both" ? "Both" : m.convType == "forward" ? "Fwd" : "Rev";
                        sb.AppendLine($"    [{tag}] {m.avatar} ({m.price}) - {m.shop}");
                        sb.AppendLine($"      BOOTH: https://booth.pm/ja/items/{m.boothId}");
                    }
                    if (matches.Count > 3)
                        sb.AppendLine($"    ... and {matches.Count - 3} more");
                }
                else
                {
                    sb.AppendLine($"  [NOT FOUND] No MochiFitter profile available.");

                    // Try to find similar names
                    var similar = FindSimilarAvatars(catalog, avatarName, 3);
                    if (similar.Count > 0)
                    {
                        sb.AppendLine($"  Similar names in catalog:");
                        foreach (var s in similar)
                            sb.AppendLine($"    - {s.avatar} ({s.shop})");
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        [AgentTool("Recommend a MochiFitter profile for outfit fitting. Given a target avatar and source outfit avatar, finds the best profile to convert between them. Considers conversion direction (forward/reverse/both) and recommends the optimal profile.\ntargetAvatar: the avatar you want to dress (e.g. 'しなの')\nsourceOutfitAvatar: the avatar the outfit was originally made for (e.g. 'マヌカ')")]
        public static string RecommendMochiFitterProfile(
            string targetAvatar,
            string sourceOutfitAvatar = "")
        {
            var catalog = MochiFitterCatalogWindow.LoadCatalogStatic();
            if (catalog == null || catalog.profiles.Count == 0)
                return "MochiFitter catalog is empty or not found.";

            var sb = new StringBuilder();
            string target = (targetAvatar ?? "").Trim().ToLower();

            // Find profiles for the target avatar
            var targetProfiles = catalog.profiles.Where(p =>
            {
                string av = (p.avatar ?? "").ToLower();
                return av.Contains(target) || target.Contains(av);
            }).ToList();

            if (targetProfiles.Count == 0)
            {
                sb.AppendLine($"No MochiFitter profile found for target avatar '{targetAvatar}'.");
                sb.AppendLine("Without a profile, MochiFitter cannot retarget outfits to this avatar.");

                var similar = FindSimilarAvatars(catalog, targetAvatar, 5);
                if (similar.Count > 0)
                {
                    sb.AppendLine($"\nDid you mean one of these?");
                    foreach (var s in similar)
                        sb.AppendLine($"  - {s.avatar} ({s.price}, {s.convType}) BOOTH: https://booth.pm/ja/items/{s.boothId}");
                }
                return sb.ToString();
            }

            sb.AppendLine($"=== MochiFitter Recommendation for '{targetAvatar}' ===");
            sb.AppendLine();

            // Categorize by capability
            var bothProfiles = targetProfiles.Where(p => p.convType == "both").ToList();
            var forwardProfiles = targetProfiles.Where(p => p.convType == "forward").ToList();
            var reverseProfiles = targetProfiles.Where(p => p.convType == "reverse").ToList();

            // Best recommendation
            if (bothProfiles.Count > 0)
            {
                var best = bothProfiles.OrderBy(p => IsFree(p.price) ? 0 : 1).First();
                sb.AppendLine($"[RECOMMENDED] {best.avatar} ({best.price}) - {best.shop}");
                sb.AppendLine($"  Type: Both directions (can wear other outfits AND share outfits)");
                sb.AppendLine($"  BOOTH: https://booth.pm/ja/items/{best.boothId}");
                sb.AppendLine();
            }

            if (forwardProfiles.Count > 0)
            {
                sb.AppendLine($"Forward-only profiles ({forwardProfiles.Count}):");
                sb.AppendLine($"  (Can wear other avatars' outfits on {targetAvatar})");
                foreach (var p in forwardProfiles.OrderBy(p => IsFree(p.price) ? 0 : 1).Take(3))
                    sb.AppendLine($"  - {p.avatar} ({p.price}) - {p.shop} | BOOTH: https://booth.pm/ja/items/{p.boothId}");
                sb.AppendLine();
            }

            if (reverseProfiles.Count > 0)
            {
                sb.AppendLine($"Reverse-only profiles ({reverseProfiles.Count}):");
                sb.AppendLine($"  (Can share {targetAvatar}'s outfits to other avatars)");
                foreach (var p in reverseProfiles.Take(3))
                    sb.AppendLine($"  - {p.avatar} ({p.price}) - {p.shop} | BOOTH: https://booth.pm/ja/items/{p.boothId}");
                sb.AppendLine();
            }

            // If source outfit avatar specified, check if it also has a profile
            if (!string.IsNullOrWhiteSpace(sourceOutfitAvatar))
            {
                string source = sourceOutfitAvatar.Trim().ToLower();
                var sourceProfiles = catalog.profiles.Where(p =>
                {
                    string av = (p.avatar ?? "").ToLower();
                    return av.Contains(source) || source.Contains(av);
                }).ToList();

                sb.AppendLine($"--- Source outfit avatar: '{sourceOutfitAvatar}' ---");
                if (sourceProfiles.Count > 0)
                {
                    var sourceReverse = sourceProfiles.Where(p => p.convType == "both" || p.convType == "reverse").ToList();
                    if (sourceReverse.Count > 0)
                    {
                        sb.AppendLine($"  [OK] Source avatar has reverse/both profile - outfit sharing is supported.");
                        foreach (var p in sourceReverse.Take(2))
                            sb.AppendLine($"    {p.avatar} ({p.price}, {p.convType}) BOOTH: https://booth.pm/ja/items/{p.boothId}");
                    }
                    else
                    {
                        sb.AppendLine($"  [WARNING] Source avatar only has forward profiles - reverse fitting may not work well.");
                    }
                }
                else
                {
                    sb.AppendLine($"  [NOT FOUND] No profile for source avatar '{sourceOutfitAvatar}'.");
                }
            }

            return sb.ToString();
        }

        // ── Helpers ─────────────────────────────────────────────────

        private static void AppendProfileInfo(StringBuilder sb, MochiFitterProfileEntry p)
        {
            string tag = MochiFitterEntryType.Normalize(p.type) == MochiFitterEntryType.AvatarNative
                ? "Avatar"
                : p.convType == "both" ? "Both" : p.convType == "forward" ? "Fwd" : p.convType == "reverse" ? "Rev" : "?";
            sb.AppendLine($"  [{tag}] {p.avatar} ({p.price}) - {p.shop}");
            sb.AppendLine($"    BOOTH: https://booth.pm/ja/items/{p.boothId}");
        }

        private static List<MochiFitterProfileEntry> FindSimilarAvatars(
            MochiFitterCatalogRoot catalog, string query, int maxResults)
        {
            string q = (query ?? "").Trim().ToLower();
            if (string.IsNullOrEmpty(q)) return new List<MochiFitterProfileEntry>();

            // Score by character overlap
            return catalog.profiles
                .Select(p => new { Profile = p, Score = ComputeSimilarity(q, (p.avatar ?? "").ToLower()) })
                .Where(x => x.Score > 0.2f)
                .OrderByDescending(x => x.Score)
                .Take(maxResults)
                .Select(x => x.Profile)
                .ToList();
        }

        private static float ComputeSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0f;

            // Bigram similarity
            var bigramsA = new HashSet<string>();
            for (int i = 0; i < a.Length - 1; i++)
                bigramsA.Add(a.Substring(i, 2));

            int matches = 0;
            for (int i = 0; i < b.Length - 1; i++)
                if (bigramsA.Contains(b.Substring(i, 2)))
                    matches++;

            int total = bigramsA.Count + (b.Length - 1);
            return total > 0 ? (2f * matches) / total : 0f;
        }

        private static bool LevenshteinClose(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            // Simple threshold: if bigram similarity > 0.4, consider close
            return ComputeSimilarity(a, b) > 0.4f;
        }

        private static bool IsFree(string price)
        {
            if (string.IsNullOrEmpty(price)) return false;
            return price == "無料" || price == "¥0" || price == "0" || price.Contains("無料");
        }
    }
}
