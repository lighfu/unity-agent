using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// VRChat SDK publishing tools: authentication status, uploaded-content listing
    /// (Content Manager equivalent) and Build &amp; Publish for avatars/worlds.
    ///
    /// All VRChat SDK types are resolved via reflection so this package still compiles
    /// in projects without the VRChat SDK (same convention as VRChatTools).
    ///
    /// Public visibility is double-gated: the confirmPublic parameter AND a native
    /// modal dialog the human must accept — the calling LLM cannot make content
    /// public on its own.
    /// </summary>
    public static class VRChatUploadTools
    {
        private const string ApiUserTypeName = "VRC.Core.APIUser";
        private const string ApiCredentialsTypeName = "VRC.Core.ApiCredentials";
        private const string PipelineManagerTypeName = "VRC.Core.PipelineManager";
        // VRCSdkControlPanel lives in the global namespace.
        private const string ControlPanelTypeName = "VRCSdkControlPanel";
        private const string VrcApiTypeName = "VRC.SDKBase.Editor.Api.VRCApi";
        private const string VrcAvatarTypeName = "VRC.SDKBase.Editor.Api.VRCAvatar";
        private const string VrcWorldTypeName = "VRC.SDKBase.Editor.Api.VRCWorld";
        private const string AvatarBuilderInterfaceName = "VRC.SDK3A.Editor.IVRCSdkAvatarBuilderApi";
        private const string WorldBuilderInterfaceName = "VRC.SDK3.Editor.IVRCSdkWorldBuilderApi";
        private const string SceneDescriptorTypeName = "VRC.SDKBase.VRC_SceneDescriptor";

        private const int ApiCallTimeoutSeconds = 60;

        // ─── Authentication ───

        [AgentTool("Check whether the VRChat SDK session is authenticated (logged in). Reports only the status, display name and publish permissions — never credentials. " +
            "If not logged in but saved credentials exist and autoInitialize=true, opens the VRChat SDK Control Panel and waits up to timeoutSeconds for the session to restore. " +
            "Requires the VRChat SDK.",
            Category = "VRChat Publishing")]
        public static IEnumerator CheckVRChatAuthentication(bool autoInitialize = true, int timeoutSeconds = 20)
        {
            var apiUserType = VRChatTools.FindVrcType(ApiUserTypeName);
            if (apiUserType == null)
            {
                yield return "Error: VRChat SDK not found. Ensure the VRChat SDK (com.vrchat.avatars or com.vrchat.worlds) is installed.";
                yield break;
            }

            if (IsLoggedIn(apiUserType))
            {
                yield return DescribeAuthState(apiUserType);
                yield break;
            }

            bool hasSavedCredentials;
            try { hasSavedCredentials = TryLoadSavedCredentials(); }
            catch { hasSavedCredentials = false; }

            if (!hasSavedCredentials)
            {
                yield return "Not authenticated: no active VRChat session and no saved credentials. " +
                             "Open the VRChat SDK Control Panel (OpenVRChatSdkControlPanel) and ask the user to log in manually.";
                yield break;
            }

            if (!autoInitialize)
            {
                yield return "Not authenticated (yet): saved VRChat credentials exist but the session is not initialized in this Editor session. " +
                             "Call CheckVRChatAuthentication with autoInitialize=true, or open the SDK Control Panel to restore the session.";
                yield break;
            }

            // The SDK restores the saved session from VRCSdkControlPanel's own init path,
            // so opening the panel is the supported way to trigger it.
            string openError = EnsureControlPanelOpen();
            if (openError != null) { yield return openError; yield break; }

            double deadline = EditorApplication.timeSinceStartup + Mathf.Clamp(timeoutSeconds, 3, 120);
            while (!IsLoggedIn(apiUserType) && EditorApplication.timeSinceStartup < deadline)
                yield return null;

            if (IsLoggedIn(apiUserType))
                yield return DescribeAuthState(apiUserType);
            else
                yield return $"Not authenticated: saved credentials exist but the session did not restore within {Mathf.Clamp(timeoutSeconds, 3, 120)}s. " +
                             "The saved session may have expired — ask the user to log in via the VRChat SDK Control Panel (already opened).";
        }

        [AgentTool("Open (and focus) the VRChat SDK Control Panel window. Use it to let the user log in, or to prepare the SDK builders before uploading. Requires the VRChat SDK.",
            Category = "VRChat Publishing")]
        public static string OpenVRChatSdkControlPanel()
        {
            string error = EnsureControlPanelOpen();
            return error ?? "Success: VRChat SDK Control Panel is open.";
        }

        // ─── Content Manager ───

        [AgentTool("List the current user's uploaded VRChat content from the VRChat API (equivalent of the SDK Content Manager tab). " +
            "contentType: 'avatars', 'worlds' or 'both'. Returns ID, name, visibility (releaseStatus), version, last update and platforms per item. " +
            "count: max items per type (1-100). Requires a logged-in VRChat SDK session (see CheckVRChatAuthentication).",
            Category = "VRChat Publishing")]
        public static IEnumerator ListVRChatUploadedContent(string contentType = "both", int count = 20, int offset = 0)
        {
            contentType = (contentType ?? "").Trim().ToLowerInvariant();
            if (contentType != "avatars" && contentType != "worlds" && contentType != "both")
            {
                yield return "Error: contentType must be 'avatars', 'worlds' or 'both'.";
                yield break;
            }
            count = Mathf.Clamp(count, 1, 100);
            offset = Mathf.Max(0, offset);

            string guard = RequireSdkAndLogin(out _);
            if (guard != null) { yield return guard; yield break; }

            // In 'both' mode a failure on one side must not throw away the other side's
            // already-fetched results — degrade to a warning instead.
            bool partial = contentType == "both";
            var sb = new StringBuilder();
            if (contentType == "avatars" || contentType == "both")
            {
                var res = new TaskResult();
                var drive = FetchContentList("avatars", VrcAvatarTypeName, count, offset, res);
                while (drive.MoveNext()) yield return drive.Current;
                if (res.Error != null)
                {
                    if (!partial) { yield return res.Error; yield break; }
                    sb.AppendLine($"Warning: avatar list failed — {res.Error}");
                    sb.AppendLine();
                }
                else
                {
                    AppendAvatarList(sb, res.Value as IEnumerable, offset);
                }
            }
            if (contentType == "worlds" || contentType == "both")
            {
                var res = new TaskResult();
                var drive = FetchContentList("worlds", VrcWorldTypeName, count, offset, res);
                while (drive.MoveNext()) yield return drive.Current;
                if (res.Error != null)
                {
                    if (!partial) { yield return res.Error; yield break; }
                    sb.AppendLine($"Warning: world list failed — {res.Error}");
                }
                else
                {
                    AppendWorldList(sb, res.Value as IEnumerable, offset);
                }
            }
            yield return sb.ToString().TrimEnd();
        }

        [AgentTool("Get detailed info for one uploaded VRChat content item by ID (avtr_... or wrld_...): name, description, visibility, tags, version, timestamps, author and platforms. " +
            "Requires a logged-in VRChat SDK session.",
            Category = "VRChat Publishing")]
        public static IEnumerator GetVRChatContentInfo(string contentId)
        {
            contentId = (contentId ?? "").Trim();
            bool isAvatar = contentId.StartsWith("avtr_");
            bool isWorld = contentId.StartsWith("wrld_");
            if (!isAvatar && !isWorld)
            {
                yield return "Error: contentId must start with 'avtr_' (avatar) or 'wrld_' (world).";
                yield break;
            }

            string guard = RequireSdkAndLogin(out _);
            if (guard != null) { yield return guard; yield break; }

            var res = new TaskResult();
            var drive = InvokeApiTask(isAvatar ? "GetAvatar" : "GetWorld",
                new object[] { contentId, true, CancellationToken.None }, res);
            while (drive.MoveNext()) yield return drive.Current;
            if (res.Error != null) { yield return res.Error; yield break; }

            yield return isAvatar ? DescribeAvatarDetail(res.Value) : DescribeWorldDetail(res.Value);
        }

        // ─── Upload ───

        [AgentTool("Build and publish an avatar to VRChat (SDK 'Build & Publish'). Visibility defaults to PRIVATE. " +
            "visibility='public' requires confirmPublic=true (set it only after the human user explicitly approved, e.g. via AskUser) AND a native dialog the user must accept. " +
            "NEW avatars (PipelineManager has no blueprint ID) require contentName and thumbnailPath (png/jpg, cropped to 800x600 by the SDK). " +
            "Updates keep the existing name/description/tags/visibility unless overridden; thumbnailPath is optional. " +
            "tags: comma separated; plain words get the author_tag_ prefix automatically. " +
            "The SDK Control Panel opens automatically and the SDK may show its copyright-agreement dialog once per session. " +
            "Refuses to run in batch mode (the consent dialogs cannot be shown there). " +
            "Runs a full SDK/NDMF build plus network upload — can take several minutes.",
            Category = "VRChat Publishing", Risk = ToolRisk.Dangerous)]
        public static IEnumerator UploadVRChatAvatar(
            string avatarRootName,
            string contentName = "",
            string description = "",
            string tags = "",
            string visibility = "",
            string thumbnailPath = "",
            bool confirmPublic = false,
            int timeoutSeconds = 1800)
        {
            string interactive = RequireInteractiveSession();
            if (interactive != null) { yield return interactive; yield break; }

            var builderInterface = VRChatTools.FindVrcType(AvatarBuilderInterfaceName);
            var avatarType = VRChatTools.FindVrcType(VrcAvatarTypeName);
            var pmType = VRChatTools.FindVrcType(PipelineManagerTypeName);
            if (builderInterface == null || avatarType == null || pmType == null)
            {
                yield return "Error: VRChat Avatars SDK not found. Ensure com.vrchat.avatars is installed.";
                yield break;
            }

            string guard = RequireSdkAndLogin(out var apiUserType);
            if (guard != null) { yield return guard; yield break; }

            object currentUser = GetStaticProp(apiUserType, "CurrentUser");
            if (!(GetProp(currentUser, "canPublishAvatars") is bool canPublish && canPublish))
            {
                yield return "Error: This VRChat account is not allowed to publish avatars yet (trust rank too low, or user info not loaded).";
                yield break;
            }

            visibility = (visibility ?? "").Trim().ToLowerInvariant();
            if (visibility != "" && visibility != "private" && visibility != "public")
            {
                yield return "Error: visibility must be 'private' or 'public' (or omitted to keep the current value).";
                yield break;
            }

            var go = MeshAnalysisTools.FindGameObject(avatarRootName);
            if (go == null) { yield return $"Error: GameObject '{avatarRootName}' not found."; yield break; }
            if (VRChatTools.FindAvatarDescriptor(avatarRootName) == null)
            {
                yield return $"Error: No VRCAvatarDescriptor found on '{avatarRootName}'.";
                yield break;
            }

            var pm = go.GetComponent(pmType);
            if (pm == null) pm = Undo.AddComponent(go, pmType);
            var blueprintField = pmType.GetField("blueprintId");
            string blueprintId = blueprintField?.GetValue(pm) as string ?? "";
            bool isNew = string.IsNullOrWhiteSpace(blueprintId);

            string resolvedThumbnail = null;
            if (!string.IsNullOrWhiteSpace(thumbnailPath))
            {
                resolvedThumbnail = ResolveProjectFile(thumbnailPath);
                if (resolvedThumbnail == null)
                {
                    yield return $"Error: Thumbnail file not found: '{thumbnailPath}'.";
                    yield break;
                }
            }

            if (isNew)
            {
                // Validate up front: the SDK only rejects a missing thumbnail AFTER reserving
                // the avatar record and building, which would leave an orphan record behind.
                if (string.IsNullOrWhiteSpace(contentName))
                {
                    yield return "Error: contentName is required when uploading a NEW avatar (no blueprint ID yet).";
                    yield break;
                }
                if (resolvedThumbnail == null)
                {
                    yield return "Error: thumbnailPath is required when uploading a NEW avatar (the VRChat API needs a thumbnail image).";
                    yield break;
                }
            }

            object avatarData;
            string previousStatus = null;
            if (isNew)
            {
                avatarData = Activator.CreateInstance(avatarType);
                SetProp(avatarData, "Name", contentName.Trim());
                SetProp(avatarData, "Description", description ?? "");
                SetProp(avatarData, "Tags", ParseTags(tags));
                SetProp(avatarData, "ReleaseStatus", visibility == "" ? "private" : visibility);
            }
            else
            {
                // Update path: start from the remote record so untouched fields keep their
                // values, and so the SDK's ContentInfoEqual never sees null Name/Tags.
                var fetch = new TaskResult();
                var fetchDrive = InvokeApiTask("GetAvatar", new object[] { blueprintId, true, CancellationToken.None }, fetch);
                while (fetchDrive.MoveNext()) yield return fetchDrive.Current;
                if (fetch.Error != null)
                {
                    yield return fetch.Error +
                        $"\nHint: PipelineManager.blueprintId is '{blueprintId}'. If that avatar was deleted on VRChat, clear the blueprint ID to upload as a new avatar.";
                    yield break;
                }
                avatarData = fetch.Value;
                previousStatus = GetProp(avatarData, "ReleaseStatus") as string;
                if (!string.IsNullOrWhiteSpace(contentName)) SetProp(avatarData, "Name", contentName.Trim());
                if (!string.IsNullOrWhiteSpace(description)) SetProp(avatarData, "Description", description);
                if (!string.IsNullOrWhiteSpace(tags)) SetProp(avatarData, "Tags", ParseTags(tags));
                if (visibility != "") SetProp(avatarData, "ReleaseStatus", visibility);
                if (GetProp(avatarData, "Description") == null) SetProp(avatarData, "Description", "");
                if (GetProp(avatarData, "Tags") == null) SetProp(avatarData, "Tags", new List<string>());
            }

            string finalStatus = GetProp(avatarData, "ReleaseStatus") as string ?? "private";
            bool becomesPublic = finalStatus == "public" && (isNew || previousStatus != "public");
            if (becomesPublic)
            {
                string gateError = GatePublicRelease("avatar", GetProp(avatarData, "Name") as string ?? avatarRootName, confirmPublic);
                if (gateError != null) { yield return gateError; yield break; }
            }

            string openError = EnsureControlPanelOpen();
            if (openError != null) { yield return openError; yield break; }
            yield return null; // let the panel finish OnEnable before asking it for builders

            object builder;
            string builderError = null;
            try { builder = GetSdkBuilder(builderInterface, out builderError); }
            catch (Exception e) { builder = null; builderError = $"Error: TryGetBuilder failed: {Describe(e)}"; }
            if (builder == null)
            {
                yield return builderError ?? "Error: Could not acquire the VRChat avatar builder from the SDK Control Panel.";
                yield break;
            }

            MethodInfo buildAndUpload = null;
            foreach (var m in builderInterface.GetMethods())
            {
                if (m.Name != "BuildAndUpload") continue;
                var ps = m.GetParameters();
                // Pick the (GameObject, VRCAvatar, string, CancellationToken) overload explicitly —
                // a second overload takes per-platform override options.
                if (ps.Length == 4 && ps[0].ParameterType == typeof(GameObject) && ps[1].ParameterType == avatarType)
                {
                    buildAndUpload = m;
                    break;
                }
            }
            if (buildAndUpload == null)
            {
                yield return "Error: IVRCSdkAvatarBuilderApi.BuildAndUpload(GameObject, VRCAvatar, string, CancellationToken) not found (VRChat SDK version mismatch?).";
                yield break;
            }

            var progress = new UploadProgressTracker();
            var cts = new CancellationTokenSource();
            var result = new TaskResult();
            bool finished = false;
            // try/finally (no catch, so yields are legal) — when the user cancels the tool
            // mid-run the invoker disposes this iterator, and the finally is the only place
            // that still cancels the SDK task and detaches the progress handler.
            try
            {
                progress.Attach(builder);

                Task task = null;
                try
                {
                    task = (Task)buildAndUpload.Invoke(builder, new object[] { go, avatarData, resolvedThumbnail, cts.Token });
                }
                catch (Exception e)
                {
                    result.Error = $"Error: BuildAndUpload failed to start: {Describe(e)}";
                }

                if (result.Error == null && task == null)
                    result.Error = "Error: BuildAndUpload did not return a task.";

                if (result.Error == null)
                {
                    ToolProgress.Report(0.02f, "VRChat アップロード準備中...", "SDK の同意ダイアログが表示された場合は応答してください");
                    var awaiter = AwaitUploadTask(task, cts, Mathf.Clamp(timeoutSeconds, 60, 7200), builder, progress, result);
                    while (awaiter.MoveNext()) yield return awaiter.Current;
                }
                finished = true;
            }
            finally
            {
                if (!finished)
                {
                    try { cts.Cancel(); } catch { }
                }
                progress.Detach();
                cts.Dispose();
                ToolProgress.Clear();
            }

            if (result.Error != null)
            {
                yield return result.Error;
                yield break;
            }

            string finalBlueprintId = blueprintField?.GetValue(pm) as string ?? blueprintId;
            var summary = new StringBuilder();
            summary.Append(isNew
                ? $"Success: NEW avatar '{GetProp(avatarData, "Name")}' uploaded to VRChat. ID: {finalBlueprintId}."
                : $"Success: Avatar '{GetProp(avatarData, "Name")}' ({finalBlueprintId}) updated on VRChat.");
            summary.Append($" Visibility: {finalStatus}.");
            summary.Append($" Platform: {EditorUserBuildSettings.activeBuildTarget}.");
            if (!isNew && previousStatus == "public" && finalStatus == "public")
                summary.Append(" Note: this avatar is PUBLIC — the update is immediately visible to everyone.");
            yield return summary.ToString();
        }

        [AgentTool("Build and publish the currently open world scene to VRChat. Worlds always upload as PRIVATE first (VRChat rule); " +
            "visibility='public' additionally submits the world to Community Labs after the upload and requires confirmPublic=true (set only after explicit user approval) plus a native dialog the user must accept. " +
            "NEW worlds (scene PipelineManager has no blueprint ID) require contentName and thumbnailPath. " +
            "tags: comma separated; plain words get the author_tag_ prefix. capacity: 0 keeps the current/default value. " +
            "Requires com.vrchat.worlds, a scene containing exactly one VRC_SceneDescriptor/PipelineManager, and a logged-in SDK session. " +
            "Refuses to run in batch mode (the consent dialogs cannot be shown there). " +
            "Runs a full scene build plus upload — can take several minutes.",
            Category = "VRChat Publishing", Risk = ToolRisk.Dangerous)]
        public static IEnumerator UploadVRChatWorld(
            string contentName = "",
            string description = "",
            string tags = "",
            string visibility = "",
            string thumbnailPath = "",
            int capacity = 0,
            bool confirmPublic = false,
            int timeoutSeconds = 1800)
        {
            string interactive = RequireInteractiveSession();
            if (interactive != null) { yield return interactive; yield break; }

            var builderInterface = VRChatTools.FindVrcType(WorldBuilderInterfaceName);
            var worldType = VRChatTools.FindVrcType(VrcWorldTypeName);
            var pmType = VRChatTools.FindVrcType(PipelineManagerTypeName);
            if (builderInterface == null || worldType == null || pmType == null)
            {
                yield return "Error: VRChat Worlds SDK not found. Ensure com.vrchat.worlds is installed (this looks like an avatar-only project).";
                yield break;
            }

            string guard = RequireSdkAndLogin(out var apiUserType);
            if (guard != null) { yield return guard; yield break; }

            object currentUser = GetStaticProp(apiUserType, "CurrentUser");
            if (!(GetProp(currentUser, "canPublishWorlds") is bool canPublish && canPublish))
            {
                yield return "Error: This VRChat account is not allowed to publish worlds yet (trust rank too low, or user info not loaded).";
                yield break;
            }

            visibility = (visibility ?? "").Trim().ToLowerInvariant();
            if (visibility != "" && visibility != "private" && visibility != "public")
            {
                yield return "Error: visibility must be 'private' or 'public' (or omitted to keep the current state).";
                yield break;
            }

            // Include inactive objects: the SDK's own pipeline (Tools.FindSceneObjectsOfTypeAll)
            // accepts a descriptor on an inactive GameObject.
            var descriptorType = VRChatTools.FindVrcType(SceneDescriptorTypeName);
            if (descriptorType == null || UnityEngine.Object.FindObjectsByType(descriptorType, FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0)
            {
                yield return "Error: The open scene has no VRC_SceneDescriptor. Open the world scene before uploading.";
                yield break;
            }
            var pms = UnityEngine.Object.FindObjectsByType(pmType, FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (pms.Length == 0)
            {
                yield return "Error: The scene has no PipelineManager (it is normally added together with VRC_SceneDescriptor).";
                yield break;
            }
            if (pms.Length > 1)
            {
                yield return $"Error: The scene has {pms.Length} PipelineManager components — the VRChat SDK requires exactly one. Remove the extras.";
                yield break;
            }
            var pm = pms[0];
            var blueprintField = pmType.GetField("blueprintId");
            string blueprintId = blueprintField?.GetValue(pm) as string ?? "";
            bool isNew = string.IsNullOrWhiteSpace(blueprintId);

            string resolvedThumbnail = null;
            if (!string.IsNullOrWhiteSpace(thumbnailPath))
            {
                resolvedThumbnail = ResolveProjectFile(thumbnailPath);
                if (resolvedThumbnail == null)
                {
                    yield return $"Error: Thumbnail file not found: '{thumbnailPath}'.";
                    yield break;
                }
            }

            if (isNew)
            {
                if (string.IsNullOrWhiteSpace(contentName))
                {
                    yield return "Error: contentName is required when uploading a NEW world (no blueprint ID yet).";
                    yield break;
                }
                if (resolvedThumbnail == null)
                {
                    yield return "Error: thumbnailPath is required when uploading a NEW world.";
                    yield break;
                }
            }

            object worldData;
            string previousStatus = null;
            if (isNew)
            {
                worldData = Activator.CreateInstance(worldType);
                SetProp(worldData, "Name", contentName.Trim());
                SetProp(worldData, "Description", description ?? "");
                SetProp(worldData, "Tags", ParseTags(tags));
                SetProp(worldData, "Capacity", capacity > 0 ? capacity : 32);
                SetProp(worldData, "RecommendedCapacity", Mathf.Min(16, capacity > 0 ? capacity : 16));
                SetProp(worldData, "ReleaseStatus", "private");
            }
            else
            {
                var fetch = new TaskResult();
                var fetchDrive = InvokeApiTask("GetWorld", new object[] { blueprintId, true, CancellationToken.None }, fetch);
                while (fetchDrive.MoveNext()) yield return fetchDrive.Current;
                if (fetch.Error != null)
                {
                    yield return fetch.Error +
                        $"\nHint: the scene PipelineManager.blueprintId is '{blueprintId}'. If that world was deleted on VRChat, clear the blueprint ID to upload as a new world.";
                    yield break;
                }
                worldData = fetch.Value;
                previousStatus = GetProp(worldData, "ReleaseStatus") as string;
                if (!string.IsNullOrWhiteSpace(contentName)) SetProp(worldData, "Name", contentName.Trim());
                if (!string.IsNullOrWhiteSpace(description)) SetProp(worldData, "Description", description);
                if (!string.IsNullOrWhiteSpace(tags)) SetProp(worldData, "Tags", ParseTags(tags));
                if (capacity > 0) SetProp(worldData, "Capacity", capacity);
                if (GetProp(worldData, "Description") == null) SetProp(worldData, "Description", "");
                if (GetProp(worldData, "Tags") == null) SetProp(worldData, "Tags", new List<string>());
            }

            // Worlds cannot be made public through the upload itself — public means
            // submitting to Community Labs via a separate endpoint after the upload.
            bool wantPublic = visibility == "public" && previousStatus != "public";
            if (wantPublic)
            {
                string gateError = GatePublicRelease("world", GetProp(worldData, "Name") as string ?? "(world)", confirmPublic);
                if (gateError != null) { yield return gateError; yield break; }
            }

            string openError = EnsureControlPanelOpen();
            if (openError != null) { yield return openError; yield break; }
            yield return null;

            object builder;
            string builderError = null;
            try { builder = GetSdkBuilder(builderInterface, out builderError); }
            catch (Exception e) { builder = null; builderError = $"Error: TryGetBuilder failed: {Describe(e)}"; }
            if (builder == null)
            {
                yield return builderError ?? "Error: Could not acquire the VRChat world builder from the SDK Control Panel.";
                yield break;
            }

            MethodInfo buildAndUpload = null;
            foreach (var m in builderInterface.GetMethods())
            {
                if (m.Name != "BuildAndUpload") continue;
                var ps = m.GetParameters();
                // Pick (VRCWorld, string thumbnailPath, CancellationToken); the 4-parameter
                // overload has an extra signature argument the SDK ignores anyway.
                if (ps.Length == 3 && ps[0].ParameterType == worldType && ps[1].ParameterType == typeof(string))
                {
                    buildAndUpload = m;
                    break;
                }
            }
            if (buildAndUpload == null)
            {
                yield return "Error: IVRCSdkWorldBuilderApi.BuildAndUpload(VRCWorld, string, CancellationToken) not found (VRChat SDK version mismatch?).";
                yield break;
            }

            var progress = new UploadProgressTracker();
            var cts = new CancellationTokenSource();
            var result = new TaskResult();
            bool finished = false;
            // Same abandonment-safety pattern as UploadVRChatAvatar (see comment there).
            try
            {
                progress.Attach(builder);

                Task task = null;
                try
                {
                    task = (Task)buildAndUpload.Invoke(builder, new object[] { worldData, resolvedThumbnail, cts.Token });
                }
                catch (Exception e)
                {
                    result.Error = $"Error: BuildAndUpload failed to start: {Describe(e)}";
                }

                if (result.Error == null && task == null)
                    result.Error = "Error: BuildAndUpload did not return a task.";

                if (result.Error == null)
                {
                    ToolProgress.Report(0.02f, "VRChat ワールドアップロード準備中...", "SDK の同意ダイアログが表示された場合は応答してください");
                    var awaiter = AwaitUploadTask(task, cts, Mathf.Clamp(timeoutSeconds, 60, 7200), builder, progress, result);
                    while (awaiter.MoveNext()) yield return awaiter.Current;
                }
                finished = true;
            }
            finally
            {
                if (!finished)
                {
                    try { cts.Cancel(); } catch { }
                }
                progress.Detach();
                cts.Dispose();
                ToolProgress.Clear();
            }

            if (result.Error != null)
            {
                yield return result.Error;
                yield break;
            }

            string worldId = blueprintField?.GetValue(pm) as string ?? blueprintId;
            var summary = new StringBuilder();
            summary.Append(isNew
                ? $"Success: NEW world '{GetProp(worldData, "Name")}' uploaded to VRChat as PRIVATE. ID: {worldId}."
                : $"Success: World '{GetProp(worldData, "Name")}' ({worldId}) updated on VRChat.");
            summary.Append($" Platform: {EditorUserBuildSettings.activeBuildTarget}.");

            if (wantPublic)
            {
                var publish = new TaskResult();
                var publishDrive = PublishWorldAfterUpload(worldId, publish);
                while (publishDrive.MoveNext()) yield return publishDrive.Current;
                summary.Append(" " + (publish.Error ?? publish.Value as string));
            }
            else if (visibility == "private" && previousStatus == "public")
            {
                var unpublish = new TaskResult();
                var unpublishDrive = InvokeApiTask("UnpublishWorld", new object[] { worldId, CancellationToken.None }, unpublish);
                while (unpublishDrive.MoveNext()) yield return unpublishDrive.Current;
                summary.Append(unpublish.Error != null
                    ? $" Warning: failed to unpublish: {unpublish.Error}"
                    : " The world was unpublished (back to private).");
            }
            yield return summary.ToString();
        }

        [AgentTool("Update metadata of already-uploaded VRChat content by ID (avtr_/wrld_) WITHOUT re-uploading: name, description, tags and visibility. " +
            "Making content public requires confirmPublic=true (set only after explicit user approval) plus a native dialog the user must accept. " +
            "For worlds, visibility='public' submits to Community Labs and 'private' unpublishes. " +
            "Omitted/empty parameters keep their current values. Requires a logged-in VRChat SDK session.",
            Category = "VRChat Publishing")]
        public static IEnumerator UpdateVRChatContentInfo(
            string contentId,
            string contentName = "",
            string description = "",
            string tags = "",
            string visibility = "",
            bool confirmPublic = false)
        {
            contentId = (contentId ?? "").Trim();
            bool isAvatar = contentId.StartsWith("avtr_");
            bool isWorld = contentId.StartsWith("wrld_");
            if (!isAvatar && !isWorld)
            {
                yield return "Error: contentId must start with 'avtr_' (avatar) or 'wrld_' (world).";
                yield break;
            }

            visibility = (visibility ?? "").Trim().ToLowerInvariant();
            if (visibility != "" && visibility != "private" && visibility != "public")
            {
                yield return "Error: visibility must be 'private' or 'public' (or omitted).";
                yield break;
            }
            bool hasInfoChange = !string.IsNullOrWhiteSpace(contentName) || !string.IsNullOrWhiteSpace(description) || !string.IsNullOrWhiteSpace(tags);
            if (!hasInfoChange && visibility == "")
            {
                yield return "Error: Nothing to update — specify contentName, description, tags and/or visibility.";
                yield break;
            }

            string guard = RequireSdkAndLogin(out _);
            if (guard != null) { yield return guard; yield break; }

            var fetch = new TaskResult();
            var fetchDrive = InvokeApiTask(isAvatar ? "GetAvatar" : "GetWorld",
                new object[] { contentId, true, CancellationToken.None }, fetch);
            while (fetchDrive.MoveNext()) yield return fetchDrive.Current;
            if (fetch.Error != null) { yield return fetch.Error; yield break; }

            object data = fetch.Value;
            string previousStatus = GetProp(data, "ReleaseStatus") as string;
            if (!string.IsNullOrWhiteSpace(contentName)) SetProp(data, "Name", contentName.Trim());
            if (!string.IsNullOrWhiteSpace(description)) SetProp(data, "Description", description);
            if (!string.IsNullOrWhiteSpace(tags)) SetProp(data, "Tags", ParseTags(tags));
            if (GetProp(data, "Description") == null) SetProp(data, "Description", "");
            if (GetProp(data, "Tags") == null) SetProp(data, "Tags", new List<string>());

            var summary = new StringBuilder();

            if (isAvatar)
            {
                if (visibility != "") SetProp(data, "ReleaseStatus", visibility);
                string finalStatus = GetProp(data, "ReleaseStatus") as string ?? "private";
                if (finalStatus == "public" && previousStatus != "public")
                {
                    string gateError = GatePublicRelease("avatar", GetProp(data, "Name") as string ?? contentId, confirmPublic);
                    if (gateError != null) { yield return gateError; yield break; }
                }

                var update = new TaskResult();
                var updateDrive = InvokeApiTask("UpdateAvatarInfo", new object[] { contentId, data, CancellationToken.None }, update);
                while (updateDrive.MoveNext()) yield return updateDrive.Current;
                if (update.Error != null) { yield return update.Error; yield break; }

                summary.Append($"Success: Avatar '{GetProp(update.Value, "Name")}' ({contentId}) updated.");
                summary.Append($" Visibility: {GetProp(update.Value, "ReleaseStatus")}.");
            }
            else
            {
                // World metadata update never carries releaseStatus — public/private is a
                // separate Community Labs endpoint pair.
                // Evaluate the public gate BEFORE touching the server so a declined dialog
                // does not hide an already-applied metadata update.
                bool wantPublic = visibility == "public" && previousStatus != "public";
                if (wantPublic)
                {
                    string gateError = GatePublicRelease("world", GetProp(data, "Name") as string ?? contentId, confirmPublic);
                    if (gateError != null) { yield return gateError; yield break; }
                }

                if (hasInfoChange)
                {
                    var update = new TaskResult();
                    var updateDrive = InvokeApiTask("UpdateWorldInfo", new object[] { contentId, data, CancellationToken.None }, update);
                    while (updateDrive.MoveNext()) yield return updateDrive.Current;
                    if (update.Error != null) { yield return update.Error; yield break; }
                    summary.Append($"Success: World '{GetProp(update.Value, "Name")}' ({contentId}) updated.");
                }
                else
                {
                    summary.Append($"World '{GetProp(data, "Name")}' ({contentId}):");
                }

                if (wantPublic)
                {
                    var publish = new TaskResult();
                    var publishDrive = PublishWorldAfterUpload(contentId, publish);
                    while (publishDrive.MoveNext()) yield return publishDrive.Current;
                    summary.Append(" " + (publish.Error ?? publish.Value as string));
                }
                else if (visibility == "private" && previousStatus == "public")
                {
                    var unpublish = new TaskResult();
                    var unpublishDrive = InvokeApiTask("UnpublishWorld", new object[] { contentId, CancellationToken.None }, unpublish);
                    while (unpublishDrive.MoveNext()) yield return unpublishDrive.Current;
                    summary.Append(unpublish.Error != null
                        ? $" Warning: failed to unpublish: {unpublish.Error}"
                        : " The world was unpublished (back to private).");
                }
            }

            yield return summary.ToString();
        }

        // ─── internals: auth ───

        private static bool IsLoggedIn(Type apiUserType)
        {
            try
            {
                return GetStaticProp(apiUserType, "IsLoggedIn") is bool b && b;
            }
            catch { return false; }
        }

        private static bool TryLoadSavedCredentials()
        {
            var credType = VRChatTools.FindVrcType(ApiCredentialsTypeName);
            var load = credType?.GetMethod("Load", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            return load != null && load.Invoke(null, null) is bool b && b;
        }

        private static string DescribeAuthState(Type apiUserType)
        {
            object user = GetStaticProp(apiUserType, "CurrentUser");
            string name = GetProp(user, "displayName") as string ?? "(unknown)";
            bool canAvatars = GetProp(user, "canPublishAvatars") is bool a && a;
            bool canWorlds = GetProp(user, "canPublishWorlds") is bool w && w;
            return $"Authenticated: logged in to VRChat as '{name}'. canPublishAvatars={canAvatars}, canPublishWorlds={canWorlds}.";
        }

        private static string RequireSdkAndLogin(out Type apiUserType)
        {
            apiUserType = VRChatTools.FindVrcType(ApiUserTypeName);
            if (apiUserType == null)
                return "Error: VRChat SDK not found. Ensure the VRChat SDK is installed.";
            if (!IsLoggedIn(apiUserType))
                return "Error: Not logged in to VRChat. Run CheckVRChatAuthentication first (it can restore a saved session), " +
                       "or ask the user to log in via the SDK Control Panel (OpenVRChatSdkControlPanel).";
            return null;
        }

        private static string EnsureControlPanelOpen()
        {
            var panelType = VRChatTools.FindVrcType(ControlPanelTypeName);
            if (panelType == null)
                return "Error: VRChat SDK not found. Ensure the VRChat SDK (com.vrchat.avatars or com.vrchat.worlds) is installed.";
            try
            {
                // GetWindow creates the window if needed; the panel registers itself in its
                // constructor, so TryGetBuilder works right afterwards.
                EditorWindow.GetWindow(panelType);
                return null;
            }
            catch (Exception e)
            {
                return $"Error: Failed to open the VRChat SDK Control Panel: {e.Message}";
            }
        }

        // ─── internals: public-release gate ───

        /// <summary>
        /// Uploads may only run where a human can actually click a dialog: in batch mode
        /// both the SDK's copyright-agreement dialog and our public-release dialog would
        /// auto-confirm (EditorUtility.DisplayDialog returns true headlessly).
        /// </summary>
        private static string RequireInteractiveSession()
        {
            return Application.isBatchMode
                ? "Error: Uploading is disabled in batch mode — the VRChat copyright-agreement dialog cannot be shown, " +
                  "so human consent cannot be guaranteed. Run this from an interactive Unity Editor session."
                : null;
        }

        /// <summary>
        /// Hard gate for making content public. The dialog is intentionally a native modal:
        /// only the human at the machine can approve, no tool argument can bypass it.
        /// </summary>
        private static string GatePublicRelease(string kind, string label, bool confirmPublic)
        {
            if (!confirmPublic)
                return "Error: visibility='public' requires confirmPublic=true. " +
                       "Ask the human user for explicit approval first (e.g. via the AskUser tool), then retry with confirmPublic=true.";
            // In batch mode DisplayDialog silently returns true, which would collapse the
            // double gate into the LLM-controlled flag alone — refuse instead.
            if (Application.isBatchMode)
                return "Error: public release requires an interactive Editor session (the confirmation dialog cannot be shown in batch mode).";
            bool approved = EditorUtility.DisplayDialog(
                "VRChat public 公開の確認",
                $"AI エージェントが {kind} '{SanitizeDialogLabel(label)}' を PUBLIC (全ユーザーに公開) に設定しようとしています。\n\n公開してもよろしいですか?",
                "公開する",
                "キャンセル");
            if (!approved)
                return "Cancelled by user: public release was declined in the confirmation dialog. The content stays private.";
            return null;
        }

        /// <summary>Strips control characters (newline injection) from LLM-controlled text before it is embedded in the consent dialog.</summary>
        private static string SanitizeDialogLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "(unnamed)";
            var sb = new StringBuilder(label.Length);
            foreach (var c in label)
                sb.Append(char.IsControl(c) ? ' ' : c);
            string clean = sb.ToString().Trim();
            return clean.Length > 64 ? clean.Substring(0, 64) + "…" : clean;
        }

        // ─── internals: SDK builder / task plumbing ───

        private static object GetSdkBuilder(Type builderInterface, out string error)
        {
            error = null;
            var panelType = VRChatTools.FindVrcType(ControlPanelTypeName);
            var tryGet = panelType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "TryGetBuilder" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
            if (tryGet == null)
            {
                error = "Error: VRCSdkControlPanel.TryGetBuilder not found (VRChat SDK version mismatch?).";
                return null;
            }
            var args = new object[] { null };
            bool ok = tryGet.MakeGenericMethod(builderInterface).Invoke(null, args) is bool b && b;
            if (!ok || args[0] == null)
            {
                error = $"Error: The SDK Control Panel did not provide a builder for {builderInterface.Name}. " +
                        "Make sure the panel is open and the matching SDK (avatars/worlds) is installed, then retry.";
                return null;
            }
            return args[0];
        }

        private sealed class TaskResult
        {
            public object Value;
            public string Error;
            public bool TimedOut;
        }

        private sealed class UploadProgressTracker
        {
            public volatile string Status = "";
            public float Percentage;
            private object _builder;
            private Delegate _handler;
            private EventInfo _event;

            public void Attach(object builder)
            {
                try
                {
                    _builder = builder;
                    _event = builder.GetType().GetEvent("OnSdkUploadProgress");
                    if (_event == null || _event.EventHandlerType != typeof(EventHandler<(string, float)>))
                    {
                        _event = null;
                        return;
                    }
                    _handler = new EventHandler<(string, float)>((_, e) => { Status = e.Item1; Percentage = e.Item2; });
                    _event.AddEventHandler(builder, _handler);
                }
                catch
                {
                    _event = null;
                    _handler = null;
                }
            }

            public void Detach()
            {
                try
                {
                    if (_event != null && _handler != null && _builder != null)
                        _event.RemoveEventHandler(_builder, _handler);
                }
                catch { }
                _event = null;
                _handler = null;
                _builder = null;
            }
        }

        /// <summary>
        /// Polls a VRCApi Task until completion (the SDK's async continuations run on the
        /// Unity main thread, which keeps pumping while this coroutine yields).
        /// </summary>
        private static IEnumerator AwaitTask(Task task, int timeoutSeconds, TaskResult result)
        {
            double deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
            while (!task.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup > deadline)
                {
                    result.TimedOut = true;
                    result.Error = $"Error: VRChat API call timed out after {timeoutSeconds}s.";
                    yield break;
                }
                yield return null;
            }
            FinishTask(task, result);
        }

        private static IEnumerator AwaitUploadTask(Task task, CancellationTokenSource cts, int timeoutSeconds,
            object builder, UploadProgressTracker progress, TaskResult result)
        {
            double deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
            var buildStateProp = builder.GetType().GetProperty("BuildState");
            var uploadStateProp = builder.GetType().GetProperty("UploadState");

            while (!task.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup > deadline)
                {
                    try { cts.Cancel(); } catch { }
                    double grace = EditorApplication.timeSinceStartup + 15;
                    while (!task.IsCompleted && EditorApplication.timeSinceStartup < grace)
                        yield return null;
                    // The cancel can race with the final upload step: if the task still ran to
                    // an actual outcome during the grace period, report that real outcome
                    // instead of a misleading timeout (a "cancelled" report after a completed
                    // upload would make the caller re-upload).
                    if (task.IsCompleted && !task.IsCanceled)
                    {
                        FinishTask(task, result);
                        yield break;
                    }
                    result.TimedOut = true;
                    result.Error = $"Error: Build & upload did not finish within {timeoutSeconds}s and was cancelled. " +
                                   "A copyright-agreement or validation dialog may have been waiting for input.";
                    yield break;
                }

                string uploadState = SafePropToString(uploadStateProp, builder);
                string buildState = SafePropToString(buildStateProp, builder);
                if (uploadState == "Uploading")
                    ToolProgress.Report(0.5f + Mathf.Clamp01(progress.Percentage) * 0.5f, "VRChat にアップロード中...",
                        string.IsNullOrEmpty(progress.Status) ? null : progress.Status);
                else if (buildState == "Building")
                    ToolProgress.Report(0.25f, "VRChat コンテンツをビルド中...", "NDMF/SDK ビルド実行中");
                yield return null;
            }

            if (task.IsFaulted)
            {
                result.Error = "Error: " + Describe(task.Exception);
                yield break;
            }
            if (task.IsCanceled)
                result.Error = "Error: Build & upload was cancelled.";
        }

        private static void FinishTask(Task task, TaskResult result)
        {
            if (task.IsFaulted)
            {
                result.Error = "Error: " + Describe(task.Exception);
                return;
            }
            if (task.IsCanceled)
            {
                result.Error = "Error: VRChat API call was cancelled.";
                return;
            }
            try
            {
                result.Value = task.GetType().GetProperty("Result")?.GetValue(task);
            }
            catch (Exception e)
            {
                result.Error = "Error: " + Describe(e);
            }
        }

        /// <summary>Invoke a public static VRCApi method (matched by name + arity) and await its Task.</summary>
        private static IEnumerator InvokeApiTask(string methodName, object[] args, TaskResult result)
        {
            Task task = null;
            try
            {
                var apiType = VRChatTools.FindVrcType(VrcApiTypeName);
                var method = apiType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == args.Length);
                if (method == null)
                    result.Error = $"Error: VRCApi.{methodName} not found (VRChat SDK version mismatch?).";
                else
                    task = (Task)method.Invoke(null, args);
            }
            catch (Exception e)
            {
                result.Error = $"Error: VRCApi.{methodName} failed: {Describe(e)}";
            }
            if (result.Error != null || task == null) yield break;

            var wait = AwaitTask(task, ApiCallTimeoutSeconds, result);
            while (wait.MoveNext()) yield return wait.Current;
        }

        /// <summary>GET /avatars or /worlds with user=me + releaseStatus=all (Content Manager query).</summary>
        private static IEnumerator FetchContentList(string endpoint, string itemTypeName, int count, int offset, TaskResult result)
        {
            Task task = null;
            try
            {
                var apiType = VRChatTools.FindVrcType(VrcApiTypeName);
                var itemType = VRChatTools.FindVrcType(itemTypeName);
                if (apiType == null || itemType == null)
                {
                    result.Error = "Error: VRChat SDK API types not found.";
                }
                else
                {
                    var getMethod = apiType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethodDefinition && m.GetParameters().Length == 5);
                    if (getMethod == null)
                    {
                        result.Error = "Error: VRCApi.Get<T> not found (VRChat SDK version mismatch?).";
                    }
                    else
                    {
                        var listType = typeof(List<>).MakeGenericType(itemType);
                        var query = new Dictionary<string, string>
                        {
                            { "user", "me" },
                            { "releaseStatus", "all" },
                            { "n", count.ToString() },
                            { "offset", offset.ToString() },
                        };
                        // (requestUrl, queryParams, forceRefresh, allowRetry, cancellationToken)
                        task = (Task)getMethod.MakeGenericMethod(listType)
                            .Invoke(null, new object[] { endpoint, query, true, false, CancellationToken.None });
                    }
                }
            }
            catch (Exception e)
            {
                result.Error = $"Error: Failed to query VRChat API '{endpoint}': {Describe(e)}";
            }
            if (result.Error != null || task == null) yield break;

            var wait = AwaitTask(task, ApiCallTimeoutSeconds, result);
            while (wait.MoveNext()) yield return wait.Current;
        }

        private static IEnumerator PublishWorldAfterUpload(string worldId, TaskResult outcome)
        {
            var can = new TaskResult();
            var canDrive = InvokeApiTask("GetCanPublishWorld", new object[] { worldId, CancellationToken.None }, can);
            while (canDrive.MoveNext()) yield return canDrive.Current;
            if (can.Error != null)
            {
                outcome.Error = $"Warning: could not check Community Labs eligibility: {can.Error} The world stays private.";
                yield break;
            }
            if (!(can.Value is bool canPublish && canPublish))
            {
                outcome.Value = "Note: VRChat refused publishing right now (weekly Community Labs limit or eligibility) — the world stays private.";
                yield break;
            }

            var publish = new TaskResult();
            var publishDrive = InvokeApiTask("PublishWorld", new object[] { worldId, CancellationToken.None }, publish);
            while (publishDrive.MoveNext()) yield return publishDrive.Current;
            if (publish.Error != null)
                outcome.Error = $"Warning: publish to Community Labs failed: {publish.Error} The world stays private.";
            else
                outcome.Value = "The world was submitted to Community Labs (public).";
        }

        // ─── internals: reflection & formatting helpers ───

        private static object GetStaticProp(Type type, string name)
        {
            try { return type?.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null); }
            catch { return null; }
        }

        private static object GetProp(object obj, string name)
        {
            if (obj == null) return null;
            try
            {
                var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                return prop?.GetValue(obj);
            }
            catch { return null; }
        }

        // PropertyInfo.SetValue on a boxed struct mutates the box itself, so the same
        // boxed VRCAvatar/VRCWorld instance can be handed straight to Invoke afterwards.
        private static void SetProp(object obj, string name, object value)
        {
            if (obj == null) return;
            var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null && prop.CanWrite)
                prop.SetValue(obj, value);
        }

        private static string SafePropToString(PropertyInfo prop, object target)
        {
            try { return prop?.GetValue(target)?.ToString(); }
            catch { return null; }
        }

        private static string Describe(Exception ex)
        {
            while (true)
            {
                if (ex is AggregateException agg && agg.InnerException != null) { ex = agg.InnerException; continue; }
                if (ex is TargetInvocationException tie && tie.InnerException != null) { ex = tie.InnerException; continue; }
                break;
            }
            if (ex == null) return "Unknown error";
            string apiMessage = null;
            try { apiMessage = ex.GetType().GetProperty("ErrorMessage")?.GetValue(ex) as string; }
            catch { }
            return $"{ex.GetType().Name}: {(string.IsNullOrEmpty(apiMessage) ? ex.Message : apiMessage)}";
        }

        private static List<string> ParseTags(string tags)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(tags)) return list;
            foreach (var raw in tags.Split(','))
            {
                var tag = raw.Trim();
                if (tag.Length == 0) continue;
                // The SDK stores user tags with the author_tag_ prefix; content warnings
                // (content_*) may be passed through in full form.
                if (!tag.StartsWith("author_tag_") && !tag.StartsWith("content_") && !tag.StartsWith("system_"))
                    tag = "author_tag_" + tag;
                if (!list.Contains(tag)) list.Add(tag);
            }
            return list;
        }

        private static string ResolveProjectFile(string path)
        {
            try
            {
                path = path.Trim().Replace('\\', '/');
                string full = Path.IsPathRooted(path)
                    ? path
                    : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
                return File.Exists(full) ? full : null;
            }
            catch { return null; }
        }

        private static string FormatDate(object value)
        {
            return value is DateTime dt && dt > DateTime.MinValue.AddDays(1)
                ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "?";
        }

        private static string DescribePlatforms(object unityPackages)
        {
            var packages = unityPackages as IEnumerable;
            if (packages == null) return "?";
            var names = new List<string>();
            foreach (var p in packages)
            {
                var platform = GetProp(p, "Platform") as string;
                if (!string.IsNullOrEmpty(platform) && !names.Contains(platform)) names.Add(platform);
            }
            return names.Count == 0 ? "?" : string.Join(", ", names);
        }

        private static void AppendAvatarList(StringBuilder sb, IEnumerable items, int offset)
        {
            var list = items?.Cast<object>().ToList() ?? new List<object>();
            sb.AppendLine($"Uploaded avatars ({list.Count} fetched, offset {offset}):");
            foreach (var item in list)
            {
                sb.AppendLine($"- {GetProp(item, "ID")} | {GetProp(item, "Name")} | {GetProp(item, "ReleaseStatus")}" +
                              $" | v{GetProp(item, "Version")} | updated {FormatDate(GetProp(item, "UpdatedAt"))}" +
                              $" | platforms: {DescribePlatforms(GetProp(item, "UnityPackages"))}");
            }
            if (list.Count == 0) sb.AppendLine("  (none)");
            sb.AppendLine();
        }

        private static void AppendWorldList(StringBuilder sb, IEnumerable items, int offset)
        {
            var list = items?.Cast<object>().ToList() ?? new List<object>();
            sb.AppendLine($"Uploaded worlds ({list.Count} fetched, offset {offset}):");
            foreach (var item in list)
            {
                sb.AppendLine($"- {GetProp(item, "ID")} | {GetProp(item, "Name")} | {GetProp(item, "ReleaseStatus")}" +
                              $" | v{GetProp(item, "Version")} | capacity {GetProp(item, "Capacity")}" +
                              $" | visits {GetProp(item, "Visits")} | favorites {GetProp(item, "Favorites")}" +
                              $" | updated {FormatDate(GetProp(item, "UpdatedAt"))}");
            }
            if (list.Count == 0) sb.AppendLine("  (none)");
            sb.AppendLine();
        }

        private static string DescribeAvatarDetail(object avatar)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Avatar {GetProp(avatar, "ID")}:");
            sb.AppendLine($"  Name: {GetProp(avatar, "Name")}");
            sb.AppendLine($"  Visibility: {GetProp(avatar, "ReleaseStatus")}");
            sb.AppendLine($"  Description: {GetProp(avatar, "Description")}");
            var tags = GetProp(avatar, "Tags") as IEnumerable;
            sb.AppendLine($"  Tags: {(tags == null ? "(none)" : string.Join(", ", tags.Cast<object>()))}");
            sb.AppendLine($"  Version: {GetProp(avatar, "Version")}");
            sb.AppendLine($"  Author: {GetProp(avatar, "AuthorName")} ({GetProp(avatar, "AuthorId")})");
            sb.AppendLine($"  Created: {FormatDate(GetProp(avatar, "CreatedAt"))}  Updated: {FormatDate(GetProp(avatar, "UpdatedAt"))}");
            sb.AppendLine($"  Platforms: {DescribePlatforms(GetProp(avatar, "UnityPackages"))}");
            sb.AppendLine($"  Image: {GetProp(avatar, "ImageUrl")}");
            return sb.ToString().TrimEnd();
        }

        private static string DescribeWorldDetail(object world)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"World {GetProp(world, "ID")}:");
            sb.AppendLine($"  Name: {GetProp(world, "Name")}");
            sb.AppendLine($"  Visibility: {GetProp(world, "ReleaseStatus")}");
            sb.AppendLine($"  Description: {GetProp(world, "Description")}");
            var tags = GetProp(world, "Tags") as IEnumerable;
            sb.AppendLine($"  Tags: {(tags == null ? "(none)" : string.Join(", ", tags.Cast<object>()))}");
            sb.AppendLine($"  Version: {GetProp(world, "Version")}");
            sb.AppendLine($"  Capacity: {GetProp(world, "Capacity")} (recommended {GetProp(world, "RecommendedCapacity")})");
            sb.AppendLine($"  Visits: {GetProp(world, "Visits")}  Favorites: {GetProp(world, "Favorites")}  Heat: {GetProp(world, "Heat")}");
            sb.AppendLine($"  Author: {GetProp(world, "AuthorName")} ({GetProp(world, "AuthorId")})");
            sb.AppendLine($"  Created: {FormatDate(GetProp(world, "CreatedAt"))}  Updated: {FormatDate(GetProp(world, "UpdatedAt"))}");
            sb.AppendLine($"  Publication: {FormatDate(GetProp(world, "PublicationDate"))}  Labs: {FormatDate(GetProp(world, "LabsPublicationDate"))}");
            sb.AppendLine($"  Platforms: {DescribePlatforms(GetProp(world, "UnityPackages"))}");
            sb.AppendLine($"  Image: {GetProp(world, "ImageUrl")}");
            return sb.ToString().TrimEnd();
        }
    }
}
