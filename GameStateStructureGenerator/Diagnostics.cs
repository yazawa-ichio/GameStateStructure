using Microsoft.CodeAnalysis;

namespace GameStateStructure.Generator
{
	internal static class Diagnostics
	{
		const string Category = "GameStateStructure";

		public static readonly DiagnosticDescriptor MustBePartial = new(
			"GSS0001",
			"ステートを partial にする必要があります",
			"'{0}' は遷移関数が自動生成されるため partial である必要があります",
			Category,
			DiagnosticSeverity.Error,
			true);

		public static readonly DiagnosticDescriptor TargetMustBeGameState = new(
			"GSS0002",
			"遷移先は GameState を継承している必要があります",
			"'{0}' は GameState を継承していないため遷移先に指定できません",
			Category,
			DiagnosticSeverity.Error,
			true);

		public static readonly DiagnosticDescriptor TargetMustBeConstructible = new(
			"GSS0003",
			"遷移先は引数なしで生成できる必要があります",
			"'{0}' は{1}ため遷移先に指定できません",
			Category,
			DiagnosticSeverity.Error,
			true);

		public static readonly DiagnosticDescriptor InvalidName = new(
			"GSS0004",
			"名前が識別子として不正です",
			"'{0}' は C# の識別子として使えないため、生成される関数名に指定できません",
			Category,
			DiagnosticSeverity.Error,
			true);

		public static readonly DiagnosticDescriptor DuplicateGeneratedMethod = new(
			"GSS0006",
			"生成される関数が重複しています",
			"'{0}' が重複して生成されます。Name を指定して区別してください",
			Category,
			DiagnosticSeverity.Error,
			true);

		public static readonly DiagnosticDescriptor SubscribeEventMustBePublic = new(
			"GSS0010",
			"SubscribeEvent は public にする必要があります",
			"'{0}' は public でないため、イベントの発行関数が生成されません",
			Category,
			DiagnosticSeverity.Warning,
			true);

		public static readonly DiagnosticDescriptor PublishEventHasNoSubscriber = new(
			"GSS0011",
			"PublishEvent の対象に購読関数がありません",
			"'{0}' に SubscribeEvent が付いた public 関数が無いため、発行関数が生成されません",
			Category,
			DiagnosticSeverity.Warning,
			true);

		public static readonly DiagnosticDescriptor ArgMustHaveSetter = new(
			"GSS0012",
			"Arg プロパティには setter が必要です",
			"'{0}' には setter が無いため、遷移時の代入が実行時に失敗します",
			Category,
			DiagnosticSeverity.Error,
			true);

		public static readonly DiagnosticDescriptor SubscribeEventReturnTypeIsDiscarded = new(
			"GSS0013",
			"SubscribeEvent の戻り値は破棄されます",
			"'{0}' の戻り値 '{1}' は破棄されます。完了を待つ場合は Task / ValueTask / UniTask を返してください",
			Category,
			DiagnosticSeverity.Warning,
			true);

		public static readonly DiagnosticDescriptor SubscribeEventMustBeInstanceMethod = new(
			"GSS0020",
			"SubscribeEvent は インスタンスメソッドである必要があります",
			"'{0}' は static のため、発行関数の生成とツールからの実行の両方が失敗します",
			Category,
			DiagnosticSeverity.Error,
			true);

		public static readonly DiagnosticDescriptor SubscribeEventMustNotBeGeneric = new(
			"GSS0021",
			"SubscribeEvent はジェネリックメソッドにできません",
			"'{0}' はジェネリックメソッドのため、発行関数の生成とツールからの実行の両方が失敗します",
			Category,
			DiagnosticSeverity.Error,
			true);

		public static readonly DiagnosticDescriptor SubscribeEventParameterIsNotEditable = new(
			"GSS0023",
			"SubscribeEvent の引数にツールから値を用意できない型があります",
			"'{0}' の引数 '{1}' は型 '{2}' のため、EditorWindow からは実行できません。bool / int / float / string / enum のみ対応します",
			Category,
			DiagnosticSeverity.Warning,
			true);

		public static readonly DiagnosticDescriptor BranchTargetMustBeProcess = new(
			"GSS0030",
			"Branch を指定した遷移先は IProcess である必要があります",
			"'{0}' は IProcess を実装していないため Branch に入れられません。Run の完了で占有が終わる必要があります",
			Category,
			DiagnosticSeverity.Error,
			true);

		public static readonly DiagnosticDescriptor InvalidBranch = new(
			"GSS0031",
			"Branch の指定が不正です",
			"'{0}' は{1}ため Branch に指定できません",
			Category,
			DiagnosticSeverity.Error,
			true);

		public static readonly DiagnosticDescriptor GoToTargetMustNotBeProcess = new(
			"GSS0032",
			"GoTo の遷移先に IProcess は指定できません",
			"'{0}' は IProcess のため、GoTo ではなく Push で宣言してください。生成されるのは入れ替えではなく RunXxx です",
			Category,
			DiagnosticSeverity.Error,
			true);

		public static readonly DiagnosticDescriptor ProcessCanNotDeclareGoTo = new(
			"GSS0034",
			"IProcess は GoTo を宣言できません",
			"'{0}' は IProcess のため GoTo を宣言できません。寿命は Run の完了で決まります",
			Category,
			DiagnosticSeverity.Error,
			true);

		public static readonly DiagnosticDescriptor GeneratorFailure = new(
			"GSS0900",
			"コード生成に失敗しました",
			"GameStateStructure のコード生成で例外が発生しました: {0}",
			Category,
			DiagnosticSeverity.Error,
			true);
	}
}