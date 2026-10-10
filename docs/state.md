# ステートと遷移

## 遷移の宣言

| 属性 | 動き |
| --- | --- |
| `GoTo` | 自分を終了して、指定のステートに入れ替わる |
| `Push` | 指定のステートを子として追加する |

宣言すると、`GoToXxx` / `PushXxx` という遷移関数が生成されます。

生成される関数には型引数が付きます（例：`PushTitle<TitleScene>()`）。
これは宣言した型を呼び出し側にも書かせるためのもので、派生型を選ぶ機能ではありません。
`PushTitle<DerivedTitle>()` と書いても、作られるのは `TitleScene` です。
遷移先が `sealed` のときは型引数が付きません。

## 引数

プロパティに `Arg` を付けると、遷移関数の引数になります。
基底クラスに付けた `Arg` も引数になります。並び順は派生クラスの引数が先です。

`Arg(Option = true)` にすると省略できる引数になり、型は `Option<T>` になります。
`bool` や `int` は既定値（`false` や `0`）も意味のある値なので、`Option<T>` で「渡していない」を区別します。
呼び出し側は値をそのまま渡せます。

```cs
[Arg(Option = true)]
public bool Skip { get; set; } = true;

// PushInGame<InGameScene>();        Skip は true のまま
// PushInGame<InGameScene>(false);   Skip が false になる
```

## ブランチ（Branch）

同じ親の子は1つだけです。別の子を `Push` すると、前の子は終了します。
**ブランチ**を指定した子は別枠になり、通常の `Push` では終了しません。

ブランチは `Branch` を継承したクラスで作ります。クラスそのものがブランチの名前になるので、中身は空で構いません。

```cs
class HudBranch : Branch { }

class NoticeBranch : Branch
{
	public override BranchOccupied Occupied => BranchOccupied.Reject;
}

[Push(typeof(MainB))]                                    // 通常
[Push(typeof(Hud),    Branch = typeof(HudBranch))]       // ブランチ
[Push(typeof(Notice), Branch = typeof(NoticeBranch))]    // 使用中なら断るブランチ
partial class Root : GameState
{
}
```

| 指定 | 動き |
| --- | --- |
| `Branch` なし | 通常の子。別の子を `Push` すると終了する |
| `Branch` あり | そのブランチの中でだけ1つ。通常の `Push` では終了しない |
| `Occupied = Replace`（既定） | ブランチを使っているステートを終了して入れ替わる |
| `Occupied = Reject` | `BranchRejectedException` を投げて断る |

ブランチに入れられるのは `IProcess` だけです。`Run` が終わった時点でブランチが空くためです。
`IProcess` でない型を指定するとコンパイルエラーになります。

`Reject` の例外は `RunXxx` の呼び出し元に届きます。想定内の失敗なので、以後の遷移は止まりません。

```cs
try
{
	await RunNotice<Notice>();
}
catch (BranchRejectedException)
{
	// 表示中だったので出さなかった
}
```

## Process

`IProcess` を実装したステートは、子として追加されたあと `Run` が実行され、終わると自動で終了します。
`IProcess<TResult>` にすると結果を受け取れます。

- `Run` の実行中も、ほかの遷移は普通に行えます。
- 親が遷移すると、`Run` に渡された `CancellationToken` が取り消されます。

**取り消されたら例外で抜けてください。** 取り消し後に正常終了として返すと、もう終了した子に対して後片付けが走ります。
`await` の中で `OperationCanceledException` が出る書き方なら、そのままで問題ありません。

`IProcess` は `GoTo` を宣言できません。`Run` の完了を待っている側がいるので、途中で入れ替わると待ちが成り立たないためです。
同じ理由で、`GoTo` の遷移先に `IProcess` は指定できません。`Push` で宣言してください。

## 子の破棄通知

`OnPopChild` を override すると、子が終了したことを受け取れます。第2引数の `PopReason` が理由です。

| PopReason | 理由 |
| --- | --- |
| `Pop` | `Pop` で終了した |
| `GoTo` | `GoTo` で別のステートに入れ替わった |
| `Replace` | 同じ枠に別のステートが入った |
| `ProcessEnd` | `IProcess` が完了・失敗・取り消しで終わった |
| `ParentExit` | 親が終了するため一緒に終了した。**自分もこの直後に終了する** |

## イベント

子のステートから、親や祖先のステートの関数を呼ぶ仕組みです。

- 受け取る側：インターフェースか、ステートの関数に `SubscribeEvent` を付けます。
- 呼ぶ側：`PublishEvent` に、受け取る側の型を指定します。呼び出し用の関数が生成されます。

戻り値は返せません。`Task` を返す関数にすると、完了を待てます。

```cs
[SubscribeEvent]
interface IReboot
{
	void Reboot();
}

[GoTo(typeof(Root))]
partial class Root : GameState, IReboot
{
	void IReboot.Reboot()
	{
		GoToRoot<Root>();
	}

	[SubscribeEvent]
	public async Task Proc(int value)
	{
		await ProcImpl(value);
	}
}

[PublishEvent(typeof(IReboot), Prefix = "Dispatch")]
[PublishEvent(typeof(Root))]
partial class GameMain : GameState
{
	void GameOver()
	{
		// Prefix + 関数名で生成される
		DispatchReboot();
	}

	async Task Do()
	{
		// Root.Proc(3) が呼ばれる
		await Proc(3);
	}
}
```

`SubscribeEvent` を付けた関数は、EditorWindow からも実行できます。詳しくは[観測とツール](tools.md#editorからイベントを実行する)を見てください。

## 共通処理（Decorator）

フェードのように、多くのステートで同じ処理を挟みたいときに使います。
ステートに付けた目印（マーカー）を見て、対象のステートにだけ実行されます。

- マーカーが属性のとき：その属性が付いたステートが対象です。
- それ以外の型のとき：その型にキャストできるステートが対象です。

```cs
public class FadeAttribute : Attribute { }

public class FadeDecorator : Decorator<FadeAttribute>
{
	IFader Fader { get; set; }

	Dictionary<object, FadeHandle> m_Dic = new();

	protected override async Task OnPreInitialize(GameState state, FadeAttribute marker)
	{
		var handle = m_Dic[state] = Fader.Out(new FadeConfig());
		state.Context.Manage(handle);
		await handle;
	}

	protected override void OnPostEnter(GameState state, FadeAttribute marker)
	{
		if (m_Dic.TryGetValue(state, out var handle))
		{
			m_Dic.Remove(state);
			state.Context.Unmanage(handle);
			handle.Dispose();
		}
	}
}
```

### 登録先

| 登録先 | 対象 |
| --- | --- |
| `Config.Decorator` | その Manager のすべてのステート |
| `GameState.DescendantDecorators` | そのステートの子孫だけ。**自分自身は対象外** |

`DescendantDecorators` は配下のステートに処理を足すためのものです。自分の `OnEnter` で登録しても、自分には効きません。

### 解放

`OnForceRelease` を override すると、解放時の処理を書けます。
`Entry` のやり直しでも呼ばれ、完了を待てないので、**同期で終わらせてください。**

非同期版が推奨の API でも、同期版があればそちらを使います。
非同期処理を投げたままにすると、解放が終わる前に次の `Entry` が始まります。

```cs
protected override void OnForceRelease()
{
#pragma warning disable CS0618
	// 非推奨だが、待てない解放ではこちらを使う
	SceneManager.UnloadScene(m_Scene);
#pragma warning restore CS0618
}
```
