# GameStateStructure

ゲームの大枠のステートを制御するライブラリです。
遷移を属性で宣言的に記載することで、どのように遷移するのかを明示出来ます。

## ステートを作成する
GameStateを継承して遷移を属性を立てて宣言します。

```cs
[GoTo(typeof(Root))]
[Push(typeof(Boot))]
class Root : GameState
{
	protected override void OnEnter()
	{
		// SrouceGeneratorで自動生成されます。
		PushBoot<Boot>();
	}

	public void Reboot()
	{
		GoToRoot<Root>();
	}
}

[Push(typeof(Dialog))]
class Boot : GameState
{
	public async void OpenDialog()
	{
		await RunDialog("Message");
	}
}

class Dialog : GameState, IProcess
{
	[Arg]
	string Message { get; set; }

	public async Task Run(CancellationToken token)
	{
		await OpenUI(Message);
	}
}
```

### 遷移の宣言
`GoTo`は自身のステートを終了して指定のステートに遷移します。  
`Push`は子供として指定のステートを追加します。  
どちらも宣言すると自動で遷移関数が生成されます。  

### 引数に関して
ステートのプロパティに`Arg`属性を設定すると遷移時の引数になります。

### Process
`IProcess`が追加ステートをプッシュする場合とモーダルとして他の遷移をロックをかけてRun関数を実行します。  
`IProcess<TResult>`を利用すると結果を受け取れます。  

## イベントを設定する

例えば親のステートの関数を呼びたい場合があります。
その場合、組み込みイベントと使うことで手軽に実現出来ます。

イベントを受け取りたいインターフェースもしくはステートの関数に`SubscribeEvent`を宣言します。  
イベントを実行したい側は`PublishEvent`で呼び出したいイベントを持つ型を指定します。  
イベントに戻り値は設定できませんが、Task等を設定するとイベントの完了を待つことが出来ます。

```cs
[SubscribeEvent]
interface IReboot
{
	void Reboot();
}

[GoTo(typeof(Root))]
class Root : GameState, IReboot
{
	void IReboot.Reboot()
	{
		GoToRoot();
	}

	[SubscribeEvent]
	public async Task Proc(int b)
	{
		await ProcImpl(b);
	}
}

[PublishEvent(typeof(IReboot), Prefix = "Dispatch")]
[PublishEvent(typeof(Root))]
class GameMain : GameState
{
	void GameOver()
	{
		// Prefix(Dispatch) + IReboot.Reboot
		DispatchReboot();
	}

	async Task Do()
	{
		// Do Root.Proc(3)
		await Proc(3);
	}
}
```

## ステートの共通処理を実装する
ステート間のフェードなどを実装するためにDecoratorという機能があります。
ステート側のマーカー設定を見て実行されます。
マーカーにはAttributeの場合はステートに設定されている属性を取得します。それ以外の場合はマーカーにキャスト可能の場合に実行されます。

```cs
public class FadeAttribute : Attribute {}

public class FadeDecorator : Decorator<FadeAttribute>
{
	IFader Fader { get; set; }

	Dictionary<object, FadeHandle> m_Dic = new();

	protected override async Task OnPreInitialize(GameState state, FadeAttribute maker)
	{
		var handle = m_Dic[state] = Fader.Out(new FadeConfig());
		_ = state.Context.Manage(handle);
		await handle;
	}

	protected override void OnPostEnter(GameState state, FadeAttribute maker)
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