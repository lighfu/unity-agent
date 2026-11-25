# プロジェクト固有の MCP ツールを登録する

UnityAgent SDK の `[AgentTool]` を使うと、プロジェクトの Editor スクリプトを MCP から名前と引数で呼び出せます。このサンプルは保存済みのシーンを検査し、エラーと警告、参照するアセットの元ファイル容量、描画構成を一つの JSON で返します。 [シーン検査のサンプルコード](../Samples~/SceneSubmissionAuditTools.cs)

## シーン検査を登録する

1. UnityAgent を導入したプロジェクトで、サンプルを `Assets/Editor/SceneSubmissionAuditTools.cs` にコピーします。Editor 用 `.asmdef` を使う場合は、対象プラットフォームを Editor に限定し、`AjisaiFlow.UnityAgent.SDK` を参照に追加します。
2. コンパイル後に UnityAgent の **安全確認設定** を開き、外部ツールの `InspectSceneSubmission` を有効にします。MCP の公開範囲には読み取り専用ツールの `Safe` を含めます。
3. Edit Mode で対象のシーンを開き、保存します。
4. MCP の `DescribeUnityTool` に `{"name":"InspectSceneSubmission"}` を渡して登録を確認します。次に `ExecuteUnityTool` に以下の引数を渡します。`scenePath` は保存したシーンのパスに置き換えます。

```json
{
  "name": "InspectSceneSubmission",
  "arguments": {
    "scenePath": "Assets/Scenes/Example.unity",
    "maxFindings": 60
  }
}
```

## 結果の見方

| フィールド | 内容 |
| --- | --- |
| `errors`、`warnings` | スクリプト参照切れをエラーとして、有効な Renderer の空のマテリアルスロットとメッシュ参照切れを警告として数えます。 |
| `findings` | 検査項目、重要度、対象の階層パス、メッセージ。エラーを先頭に最大 `maxFindings` 件を返し、残りがある場合は `findingsTruncated` が `true` になります。 |
| `sourceAssetBytes` | シーンファイルと依存アセットの、重複を除いた元ファイル容量の合計。容量を取得できなかったパスは `assetsWithoutMeasuredSize` に数えます。 |
| `activeRenderers`、`activeMaterialSlots`、`activeMeshTriangles` | 有効な Renderer 数、そのマテリアルスロット数、MeshRenderer と SkinnedMeshRenderer の三角形数。 |

ビルド後の容量は Unity のビルドレポート、実行時の SetPass Calls と Batches は Unity Profiler で計測します。長時間かかる計測を独自ツールにする場合は、開始時に受付 ID を返し、結果取得時に同じ ID を渡せるようにします。

## 別のプロジェクトで使う

対象プロジェクトの Editor アセンブリに `public static` メソッドを置き、`[AgentTool("ツールの説明", Risk = ToolRisk.Safe)]` のように説明とリスクを指定します。引数名は MCP の入力名になります。結果を文字列で返すメソッドは、再コンパイル後に UnityAgent が自動検出します。実際の変更やビルドを行うメソッドでは、操作に合ったリスクを指定します。ツール名が重複しないように、メソッド名に対象作業を含めます。 [最小構成のサンプル](../Samples~/SampleExternalTools.cs)
