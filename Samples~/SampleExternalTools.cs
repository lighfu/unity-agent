// =============================================================================
// UnityAgent 外部ツール サンプル
//
// 使い方:
//   1. このファイルを自分の Editor フォルダにコピー
//   2. asmdef を使う場合は references に "AjisaiFlow.UnityAgent.SDK" を追加
//   3. Unity を再コンパイルすると自動的にツールが発見される
//
// ルール:
//   - クラスは public static
//   - メソッドは public static で戻り値は string（または IEnumerator で非同期）
//   - [AgentTool("説明")] 属性を付ける
//   - 引数は string, int, float, bool のみ対応（デフォルト値も可）
// =============================================================================

using UnityEngine;
using UnityEditor;
using AjisaiFlow.UnityAgent.SDK;

public static class SampleExternalTools
{
    // --- 最小構成 -----------------------------------------------------------

    [AgentTool("選択中のGameObjectのワールド座標を返します。")]
    public static string GetSelectedPosition()
    {
        var go = Selection.activeGameObject;
        if (go == null)
            return "Error: GameObjectが選択されていません。";

        var pos = go.transform.position;
        return $"{go.name} の位置: ({pos.x:F3}, {pos.y:F3}, {pos.z:F3})";
    }

    // --- メタデータ付き -----------------------------------------------------

    [AgentTool("指定したGameObjectにメモ（コメント）を付けます。",
        Author = "サンプル作者",
        Version = "1.0.0",
        Category = "メモ")]
    public static string AddNote(string gameObjectName, string note)
    {
        var go = GameObject.Find(gameObjectName);
        if (go == null)
            return $"Error: '{gameObjectName}' が見つかりません。";

        string key = $"UnityAgent_Note_{go.GetInstanceID()}";
        EditorPrefs.SetString(key, note);

        return $"Success: '{gameObjectName}' にメモを設定しました: {note}";
    }

    [AgentTool("指定したGameObjectのメモを読み取ります。",
        Author = "サンプル作者",
        Version = "1.0.0",
        Category = "メモ")]
    public static string ReadNote(string gameObjectName)
    {
        var go = GameObject.Find(gameObjectName);
        if (go == null)
            return $"Error: '{gameObjectName}' が見つかりません。";

        string key = $"UnityAgent_Note_{go.GetInstanceID()}";
        string note = EditorPrefs.GetString(key, "");

        if (string.IsNullOrEmpty(note))
            return $"'{gameObjectName}' にメモは設定されていません。";

        return $"'{gameObjectName}' のメモ: {note}";
    }

    // --- デフォルト引数の例 -------------------------------------------------

    [AgentTool("GameObjectをランダムな色のマテリアルに変更します。",
        Author = "サンプル作者",
        Version = "1.0.0",
        Category = "マテリアル")]
    public static string RandomizeMaterialColor(string gameObjectName, float saturation = 0.8f)
    {
        var go = GameObject.Find(gameObjectName);
        if (go == null)
            return $"Error: '{gameObjectName}' が見つかりません。";

        var renderer = go.GetComponent<Renderer>();
        if (renderer == null)
            return $"Error: '{gameObjectName}' に Renderer がありません。";

        Undo.RecordObject(renderer.sharedMaterial, "Randomize Material Color");

        var color = Color.HSVToRGB(Random.value, saturation, 1f);
        renderer.sharedMaterial.color = color;

        return $"Success: '{gameObjectName}' の色を ({color.r:F2}, {color.g:F2}, {color.b:F2}) に変更しました。";
    }
}
