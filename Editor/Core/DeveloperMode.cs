namespace AjisaiFlow.UnityAgent.Editor
{
    public static class DeveloperMode
    {
        private static bool? _cached;

        /// <summary>アセンブリ配置に基づく実際の開発者ビルド判定</summary>
        public static bool IsDevBuild
        {
            get
            {
                if (_cached == null)
                {
                    string location = typeof(DeveloperMode).Assembly.Location;
                    _cached = string.IsNullOrEmpty(location)
                           || location.Replace("\\", "/").Contains("ScriptAssemblies");
                }
                return _cached.Value;
            }
        }
    }
}
