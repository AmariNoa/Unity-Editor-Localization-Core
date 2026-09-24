# Unity Editor Localization Core

Unity Editor向けの共通ローカライズ基盤です。

## Git URLからのインストール

Package Managerの「Install package from git URL」に、タグ付きURLを入力します。

```text
https://github.com/AmariNoa/Unity-Editor-Localization-Core.git#v1.0.1
```

## リリース

1. バージョン用ブランチを作成したタイミングで、`package.json` の `version` を更新します（例: `v1.0.1` ブランチなら `1.0.1`）。値には `v` を付けません。
2. 変更をコミット・pushし、GitHub Actionsの **Build Release** を対象ブランチで実行します。
3. CIが実行対象コミットへ `v<version>` タグ（例: `v1.0.1`）を作成し、そのタグにGitHub Releaseと配布ファイルを紐付けます。

ZIP・unitypackageのファイル名に使うバージョンには `v` を付けません。既存のリリースタグは上書きしないため、新しいリリースには未使用のバージョンを指定してください。
