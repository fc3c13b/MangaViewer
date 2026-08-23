# TASK09.19: LoadCjForParent でリスト再構築を行う修正

## 現象

DB リスト表示モードでキー「3」を押して、既に CJ JSON が存在する親フォルダを選択すると、
内部では _activeCjData が更新されるものの、左側の ListBox（folderList）の表示が更新されない。

## 原因

KeyboardInputHandler.HandleD3Key() の動作:

- CjManager.CjExists(selectedPath) == true → nav.LoadCjForParent(selectedPath)
- false → nav.CreateCjForParent(selectedPath)

Form1.cs:

- CreateCjForParent():
  - CJ を作成・保存後、IsRankDisplayMode の場合 BuildRankFilteredListFromActiveCj() を呼び出し、ListBox を再構築している。
- LoadCjForParent():
  - JSON を読み込んで _activeCjData に設定するだけ。
  - ここで BuildRankFilteredListFromActiveCj() を呼ばないため、リストが更新されない。

つまり「既存 JSON の場合のみ」再描画処理を欠落している。

## 修正案

Form1.cs の LoadCjForParent() で _activeCjData をセットした後に、
DB リストモードなら BuildRankFilteredListFromActiveCj() を呼び出すようにする。

該当箇所（LoadCjForParent メソッド内）:

変更前（抜粋）:

    // CJ がまだない、またはロード失敗した場合は新規作成
    if (_activeCjData == null)
    {
        CreateCjForParent(parentFolder);
    }
    else
    {
        Log($"[DB_DEBUG] LoadCjForParent: loaded existing CJ for parent={parentFolder}, folders_count={_activeCjData?.Folders?.Count ?? 0}");
    }

変更後（抜粋）:

    // CJ がまだない、またはロード失敗した場合は新規作成
    if (_activeCjData == null)
    {
        CreateCjForParent(parentFolder);
    }
    else
    {
        Log($"[DB_DEBUG] LoadCjForParent: loaded existing CJ for parent={parentFolder}, folders_count={_activeCjData?.Folders?.Count ?? 0}");

        // DB リストモードなら、ロードした CJ に基づいてリストを再構築
        if (IsRankDisplayMode)
        {
            BuildRankFilteredListFromActiveCj();
        }
    }

## 影響範囲

- Form1.cs の LoadCjForParent メソッドのみ。
- CreateCjForParent との動作差を解消し、キー3で既存 JSON を含むフォルダを選択しても即座にリストが更新されるようになる。
