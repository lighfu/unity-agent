using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Skill specification system: provides .md-formatted skill instructions to the AI.
    ///
    /// Sources (in priority order):
    ///   1. Built-in skills embedded in BuiltInSkills.cs (always available, even in DLL distribution)
    ///   2. User custom skills from the Skills directory on disk (optional, for extensibility)
    ///
    /// Each skill follows this format:
    /// ---
    /// title: スキル名
    /// description: 短い説明
    /// tags: tag1, tag2, tag3
    /// ---
    /// # 本文（手順、ツール呼び出し例、注意事項など）
    /// </summary>
    public static class SkillTools
    {
        public static readonly string SkillsPath = Path.Combine(
            PackagePaths.PackageRoot, "Editor", "Skills");

        /// <summary>
        /// ユーザーカスタムスキルのパス（Assets 配下、書き込み可能）。
        /// VPM インストール時でもユーザーがここにスキルを配置できる。
        /// </summary>
        public static readonly string UserSkillsPath = Path.Combine(
            Application.dataPath, "紫陽花広場", "UnityAgent", "Editor", "Skills");

        // ─── Merged skill access ───

        /// <summary>
        /// Get all skill names and their raw content, merging built-in, package, and user custom.
        /// Priority: BuiltIn &lt; Package .md &lt; User custom .md (Assets/紫陽花広場/UnityAgent/Editor/Skills/)
        /// </summary>
        public static Dictionary<string, string> GetAllSkills()
        {
            var skills = new Dictionary<string, string>();

            // 1. Built-in skills (base)
            if (BuiltInSkills.All != null)
            {
                foreach (var kv in BuiltInSkills.All)
                    skills[kv.Key] = kv.Value;
            }

            // 2. Package skills (override built-in)
            LoadSkillsFromDir(skills, SkillsPath);

            // 3. User custom skills (highest priority — Assets/紫陽花広場/UnityAgent/Editor/Skills/)
            //    Assets 配置時は SkillsPath == UserSkillsPath なので二重スキャンをスキップ
            if (!string.Equals(Path.GetFullPath(SkillsPath), Path.GetFullPath(UserSkillsPath), System.StringComparison.OrdinalIgnoreCase))
                LoadSkillsFromDir(skills, UserSkillsPath);

            return skills;
        }

        private static void LoadSkillsFromDir(Dictionary<string, string> skills, string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*.md"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.StartsWith("_")) continue; // skip _template.md etc
                skills[name] = File.ReadAllText(file);
            }
        }

        /// <summary>
        /// Find a skill by name (case-insensitive).
        /// </summary>
        private static bool TryGetSkill(string skillName, out string name, out string content)
        {
            var skills = GetAllSkills();

            // Exact match
            if (skills.TryGetValue(skillName, out content))
            {
                name = skillName;
                return true;
            }

            // Case-insensitive match
            foreach (var kv in skills)
            {
                if (kv.Key.Equals(skillName, System.StringComparison.OrdinalIgnoreCase))
                {
                    name = kv.Key;
                    content = kv.Value;
                    return true;
                }
            }

            name = null;
            content = null;
            return false;
        }

        // ─── Agent Tools ───

        [AgentTool("List all available skill specifications. Skills are .md files that describe complex multi-step operations (e.g. avatar building, package setup, optimization). Use ReadSkill to get the full instructions for a skill.")]
        public static string ListSkills()
        {
            var skills = GetAllSkills();
            if (skills.Count == 0)
                return "No skill files found.";

            var disabled = AgentSettings.GetDisabledSkills();
            int enabledCount = skills.Count(kv => !disabled.Contains(kv.Key));

            var sb = new StringBuilder();
            sb.AppendLine($"Available Skills ({enabledCount} enabled, {skills.Count - enabledCount} disabled):");

            foreach (var kv in skills.OrderBy(k => k.Key))
            {
                var meta = ParseFrontMatter(kv.Value);
                bool isDisabled = disabled.Contains(kv.Key);

                string title = meta.ContainsKey("title") ? meta["title"] : kv.Key;
                string desc = meta.ContainsKey("description") ? meta["description"] : "";
                string tags = meta.ContainsKey("tags") ? meta["tags"] : "";

                string status = isDisabled ? " (DISABLED)" : "";
                sb.AppendLine($"  [{kv.Key}] {title}{status}");
                if (!string.IsNullOrEmpty(desc))
                    sb.AppendLine($"    {desc}");
                if (!string.IsNullOrEmpty(tags))
                    sb.AppendLine($"    Tags: {tags}");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Read a skill specification file by name (without .md extension). Returns the full instructions for performing the described operation. Example: ReadSkill('avatar-build')")]
        public static string ReadSkill(string skillName)
        {
            if (string.IsNullOrEmpty(skillName))
                return "Error: skillName is required.";

            if (!TryGetSkill(skillName, out string foundName, out string content))
                return $"Error: Skill '{skillName}' not found. Use ListSkills() to see available skills.";

            // Strip front matter for cleaner reading, but include title
            var meta = ParseFrontMatter(content);
            string body = StripFrontMatter(content);

            var sb = new StringBuilder();
            if (meta.ContainsKey("title"))
                sb.AppendLine($"# {meta["title"]}");
            sb.Append(body);

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Create a new custom skill specification file. " +
            "Parameters: skillName (kebab-case, e.g. 'my-new-skill'), title (display name), " +
            "description (one-line summary), tags (comma-separated), body (markdown content with workflow steps, tool call examples, notes). " +
            "The body should follow the standard skill format: ## 概要, ## 手順, ## ツール呼び出し例, ## 注意事項. " +
            "Example: CreateSkill('my-skill', 'マイスキル', '説明文', 'tag1, tag2', '## 概要\\n...')")]
        public static string CreateSkill(string skillName, string title, string description, string tags, string body)
        {
            if (string.IsNullOrEmpty(skillName))
                return "Error: skillName is required.";
            if (string.IsNullOrEmpty(title))
                return "Error: title is required.";

            var validationError = ValidateSkillName(skillName);
            if (validationError != null)
                return validationError;

            // 既存チェックは両パスで行う
            string packageFile = Path.Combine(SkillsPath, skillName + ".md");
            string userFile = Path.Combine(UserSkillsPath, skillName + ".md");
            if (File.Exists(userFile) || File.Exists(packageFile))
                return $"Error: Skill '{skillName}' already exists. Use UpdateSkill to modify it, or DeleteSkill first.";

            string content = BuildSkillContent(title, description ?? "", tags ?? "", body ?? "");
            EnsureUserSkillsDirectory();
            File.WriteAllText(userFile, content);

            return $"Created skill '{skillName}' at: {userFile}";
        }

        [AgentTool("Update an existing custom skill specification file. " +
            "Any parameter set to empty string will keep the current value. " +
            "To update only the body, pass empty strings for title/description/tags. " +
            "Can also override a built-in skill by creating a custom file with the same name. " +
            "Example: UpdateSkill('my-skill', '', '', '', '## 概要\\nnew content')")]
        public static string UpdateSkill(string skillName, string title, string description, string tags, string body)
        {
            if (string.IsNullOrEmpty(skillName))
                return "Error: skillName is required.";

            var validationError = ValidateSkillName(skillName);
            if (validationError != null)
                return validationError;

            // ユーザースキル → パッケージスキル → ビルトイン の順で検索
            string userFile = Path.Combine(UserSkillsPath, skillName + ".md");
            string packageFile = Path.Combine(SkillsPath, skillName + ".md");

            string existingContent = null;
            if (File.Exists(userFile))
            {
                existingContent = File.ReadAllText(userFile);
            }
            else if (File.Exists(packageFile))
            {
                existingContent = File.ReadAllText(packageFile);
            }
            else if (BuiltInSkills.All != null && BuiltInSkills.All.ContainsKey(skillName))
            {
                existingContent = BuiltInSkills.All[skillName];
            }

            if (existingContent == null)
                return $"Error: Skill '{skillName}' not found. Use CreateSkill to create a new skill.";

            var meta = ParseFrontMatter(existingContent);
            string existingBody = StripFrontMatter(existingContent);

            string newTitle = string.IsNullOrEmpty(title) ? (meta.ContainsKey("title") ? meta["title"] : skillName) : title;
            string newDesc = string.IsNullOrEmpty(description) ? (meta.ContainsKey("description") ? meta["description"] : "") : description;
            string newTags = string.IsNullOrEmpty(tags) ? (meta.ContainsKey("tags") ? meta["tags"] : "") : tags;
            string newBody = string.IsNullOrEmpty(body) ? existingBody : body;

            string content = BuildSkillContent(newTitle, newDesc, newTags, newBody);
            EnsureUserSkillsDirectory();
            File.WriteAllText(userFile, content);

            bool isOverride = BuiltInSkills.All != null && BuiltInSkills.All.ContainsKey(skillName);
            string suffix = isOverride ? " (overrides built-in skill)" : "";
            return $"Updated skill '{skillName}'{suffix} at: {userFile}";
        }

        [AgentTool("Delete a custom skill specification file. " +
            "Only user custom skills (on disk) can be deleted. Built-in skills cannot be deleted. " +
            "Example: DeleteSkill('my-skill')")]
        public static string DeleteSkill(string skillName)
        {
            if (string.IsNullOrEmpty(skillName))
                return "Error: skillName is required.";

            // ユーザースキルパスを優先、なければパッケージスキルパスを確認
            string userFile = Path.Combine(UserSkillsPath, skillName + ".md");
            string packageFile = Path.Combine(SkillsPath, skillName + ".md");
            string filePath = File.Exists(userFile) ? userFile : (File.Exists(packageFile) ? packageFile : null);

            if (filePath == null)
            {
                if (BuiltInSkills.All != null && BuiltInSkills.All.ContainsKey(skillName))
                    return $"Error: '{skillName}' is a built-in skill and cannot be deleted.";
                return $"Error: Skill file '{skillName}' not found.";
            }

            File.Delete(filePath);

            bool wasOverride = BuiltInSkills.All != null && BuiltInSkills.All.ContainsKey(skillName);
            string suffix = wasOverride ? " Built-in version is now active again." : "";
            return $"Deleted skill '{skillName}'.{suffix}";
        }

        [AgentTool("Get the skill template showing the standard format for creating skills. " +
            "Use this to understand the expected structure before calling CreateSkill.")]
        public static string GetSkillTemplate()
        {
            string templatePath = Path.Combine(SkillsPath, "_template.md");
            if (File.Exists(templatePath))
                return File.ReadAllText(templatePath);

            // Fallback built-in template
            return FallbackTemplate;
        }

        // ─── Skill management helpers ───

        private static string ValidateSkillName(string skillName)
        {
            if (skillName.StartsWith("_"))
                return "Error: Skill names starting with '_' are reserved.";

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                if (skillName.Contains(c))
                    return $"Error: Skill name contains invalid character '{c}'. Use kebab-case (e.g. 'my-skill-name').";
            }

            if (skillName.Contains(' '))
                return "Error: Skill name should not contain spaces. Use kebab-case (e.g. 'my-skill-name').";

            return null;
        }

        private static string BuildSkillContent(string title, string description, string tags, string body)
        {
            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"title: {title}");
            if (!string.IsNullOrEmpty(description))
                sb.AppendLine($"description: {description}");
            if (!string.IsNullOrEmpty(tags))
                sb.AppendLine($"tags: {tags}");
            sb.AppendLine("---");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(body))
                sb.Append(body);
            return sb.ToString();
        }

        private static void EnsureUserSkillsDirectory()
        {
            if (!Directory.Exists(UserSkillsPath))
                Directory.CreateDirectory(UserSkillsPath);
        }

        [AgentTool("Search skill specifications by keyword. Searches titles, descriptions, tags, and content.")]
        public static string SearchSkills(string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
                return "Error: keyword is required.";

            var skills = GetAllSkills();
            string kw = keyword.ToLower();

            var matches = new List<(string name, string title, string desc, int relevance)>();

            foreach (var kv in skills)
            {
                var meta = ParseFrontMatter(kv.Value);

                string title = meta.ContainsKey("title") ? meta["title"] : kv.Key;
                string desc = meta.ContainsKey("description") ? meta["description"] : "";
                string tags = meta.ContainsKey("tags") ? meta["tags"] : "";

                int relevance = 0;
                if (title.ToLower().Contains(kw)) relevance += 10;
                if (tags.ToLower().Contains(kw)) relevance += 8;
                if (desc.ToLower().Contains(kw)) relevance += 5;
                if (kv.Key.ToLower().Contains(kw)) relevance += 5;
                if (kv.Value.ToLower().Contains(kw)) relevance += 1;

                if (relevance > 0)
                    matches.Add((kv.Key, title, desc, relevance));
            }

            if (matches.Count == 0)
                return $"No skills found matching '{keyword}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Skills matching '{keyword}' ({matches.Count}):");

            foreach (var (name, title, desc, _) in matches.OrderByDescending(m => m.relevance))
            {
                sb.AppendLine($"  [{name}] {title}");
                if (!string.IsNullOrEmpty(desc))
                    sb.AppendLine($"    {desc}");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Get skill summaries for the system prompt.
        /// Called by UnityAgentCore to include skill info in the LLM context.
        /// </summary>
        public static string GetSkillSummariesForPrompt()
        {
            var skills = GetAllSkills();
            if (skills.Count == 0)
                return "";

            var disabled = AgentSettings.GetDisabledSkills();
            var enabled = skills.Where(kv => !disabled.Contains(kv.Key)).OrderBy(k => k.Key).ToList();
            if (enabled.Count == 0)
                return "";

            var sb = new StringBuilder();
            sb.AppendLine("\nAvailable Skill Specifications (use ReadSkill to read full instructions):");

            foreach (var kv in enabled)
            {
                var meta = ParseFrontMatter(kv.Value);

                string title = meta.ContainsKey("title") ? meta["title"] : kv.Key;
                string desc = meta.ContainsKey("description") ? meta["description"] : "";

                sb.AppendLine($"  - {kv.Key}: {title}{(string.IsNullOrEmpty(desc) ? "" : $" — {desc}")}");
            }

            return sb.ToString();
        }

        /// <summary>Check if a skill name exists as a built-in skill.</summary>
        public static bool IsBuiltIn(string skillName)
        {
            return BuiltInSkills.All != null && BuiltInSkills.All.ContainsKey(skillName);
        }

        /// <summary>Check if a custom file exists on disk for the skill.</summary>
        public static bool HasCustomFile(string skillName)
        {
            return File.Exists(Path.Combine(SkillsPath, skillName + ".md"))
                || File.Exists(Path.Combine(UserSkillsPath, skillName + ".md"));
        }

        // ─── Front Matter Parser ───

        public static Dictionary<string, string> ParseFrontMatter(string content)
        {
            var meta = new Dictionary<string, string>();
            if (!content.TrimStart().StartsWith("---")) return meta;

            int firstSep = content.IndexOf("---");
            int secondSep = content.IndexOf("---", firstSep + 3);
            if (secondSep < 0) return meta;

            string frontMatter = content.Substring(firstSep + 3, secondSep - firstSep - 3).Trim();
            foreach (string line in frontMatter.Split('\n'))
            {
                int colonIdx = line.IndexOf(':');
                if (colonIdx > 0)
                {
                    string key = line.Substring(0, colonIdx).Trim();
                    string value = line.Substring(colonIdx + 1).Trim();
                    meta[key] = value;
                }
            }

            return meta;
        }

        public static string StripFrontMatter(string content)
        {
            if (!content.TrimStart().StartsWith("---")) return content;

            int firstSep = content.IndexOf("---");
            int secondSep = content.IndexOf("---", firstSep + 3);
            if (secondSep < 0) return content;

            return content.Substring(secondSep + 3).TrimStart('\r', '\n');
        }

        // ─── Templates ───

        /// <summary>
        /// Build a new skill file content from the body template with user-supplied front matter.
        /// Used by SkillManagementWindow when creating a new skill from the UI.
        /// </summary>
        public static string BuildNewSkillFromTemplate(string title, string description, string tags)
        {
            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"title: {title}");
            if (!string.IsNullOrEmpty(description))
                sb.AppendLine($"description: {description}");
            if (!string.IsNullOrEmpty(tags))
                sb.AppendLine($"tags: {tags}");
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"# {title}");
            sb.AppendLine();
            sb.Append(BodyTemplate);
            return sb.ToString();
        }

        internal const string BodyTemplate =
@"## 概要

このスキルが **何を** するかを2〜3文で説明する。
- どんな場面で使うか
- 最終的に何が達成されるか

## 前提条件

- 必要なパッケージ（パッケージ名 `com.example.package`）
- 必要なコンポーネントや設定状態

## 判断フロー

<!-- 複数アプローチがある場合。単一フローなら削除 -->

| 条件 | アプローチ |
|------|-----------|
| 条件A | → 簡易手順（推奨） |
| 条件B | → 詳細手順 |

## 使用ツール一覧

| ツール名 | パラメータ | 説明 |
|---------|-----------|------|
| `ToolA` | `(param1, param2)` | 何をするか |
| `ToolB` | `(param1, opt='default')` | 何をするか |

## 手順

### ステップ1: 状態を確認する

まず現在の状態を把握する:
```
[ToolA('パラメータ')]
```
← 出力から「○○」を確認する。

### ステップ2: メインの操作

```
[ToolB('パラメータ1', 'パラメータ2')]
```
**重要**: 注意すべき点を太字で記載。

### ステップ2.5: 条件付き操作（必要時のみ）

**いつ必要か**: ステップ2の結果が○○の場合のみ

```
[ToolC('パラメータ')]
```

### ステップ3: 結果を確認する

```
[ToolD('パラメータ')]
```
← 「○○」が表示されていれば成功。

### ユーザー確認ポイント

- **ステップN後**: 「○○を確認してください。続行しますか？」
- **Scene view操作**: ユーザーにScene viewで○○をクリックしてもらう

## ツール呼び出し例

### 例1: 基本的な使い方
```
ユーザー: 「○○して」

AI:
1. [ToolA('avatarName')] → 結果を確認
2. [ToolB('avatarName', 'param')]
3. 「完了しました。○○が設定されています。」
```

### 例2: 応用（条件分岐あり）
```
ユーザー: 「○○を△△で設定して」

AI:
1. [ToolA('avatarName')] → 条件Bに該当
2. 「△△の状態なので詳細手順で進めます。」
3. [ToolE('avatarName', 'param')]
4. [ToolF('avatarName', 'param')]
```

### 例3: エラーからのリカバリ
```
AI:
1. [ToolA('avatarName')] → エラー: ○○が見つからない
2. 「○○が未設定です。先に△△を行います。」
3. [ToolG('avatarName')]
4. (ステップ1からやり直し)
```

## よくあるミス

1. **○○せずにいきなり△△する** → 先に○○で状態確認してから
2. **パラメータに✕✕を渡す** → 正しくは「○○」形式
3. **□□を忘れる** → △△の後に必ず□□を実行

## 注意事項

- 操作の安全性（非破壊的か、Undoできるか）
- パフォーマンスへの影響
- IMPORTANT: AIが絶対に間違えてはいけないルール

## 関連スキル

- `skill-name`: 説明

## トラブルシューティング

- **エラー/症状**: 原因 → 対処法
";

        private const string FallbackTemplate =
@"---
title: スキル名
description: このスキルが行うことの短い説明（1行、50文字以内）
tags: タグ1, タグ2, タグ3
---

# スキル名

" + BodyTemplate;
    }
}
