using NUnit.Framework;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// [Arg] による遷移引数の受け渡しの検証。
	/// </summary>
	public partial class ArgumentTests
	{
		[Push(typeof(Target), "Target")]
		[Push(typeof(Reserved), "Reserved")]
		partial class Root : GameState
		{
			public void DoPushRequired(string text) => PushTarget<Target>(text);

			public void DoPushWithOptions(string text, object payload, int retry)
				=> PushTarget<Target>(text, payload, retry);

			public void DoPushReserved(int parameter, string @class)
				=> PushReserved<Reserved>(parameter, @class);
		}

		partial class Target : GameState
		{
			[Arg]
			public string Text { get; set; }

			[Arg(Option = true)]
			public object Payload { get; set; } = "initial-payload";

			[Arg(Option = true)]
			public int Retry { get; set; } = 3;
		}

		/// <summary>
		/// 生成コードが自前で使う識別子や、C# のキーワードと同じ名前の引数。
		/// </summary>
		partial class Reserved : GameState
		{
			[Arg]
			public int Parameter { get; set; }

			[Arg(Option = true)]
			public string Class { get; set; } = "initial";
		}

		[Test]
		public async Task ArgumentsNamedAfterGeneratedIdentifiersAreApplied()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			context.Manager.FindState<Root>().DoPushReserved(42, "given");
			await context.WaitTransition();

			var target = context.Manager.FindState<Reserved>();
			Assert.AreEqual(42, target.Parameter, "Parameter という名前でも値が届く");
			Assert.AreEqual("given", target.Class, "キーワードと同じ名前でも値が届く");
		}

		[Test]
		public async Task RequiredArgumentsAreApplied()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			context.Manager.FindState<Root>().DoPushRequired("hello");
			await context.WaitTransition();

			var target = context.Manager.FindState<Target>();
			Assert.AreEqual("hello", target.Text);
		}

		[Test]
		public async Task OmittedOptionalArgumentsKeepInitializerValues()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			// 省略した場合は Option<T>.HasValue が false になり、Set されない
			context.Manager.FindState<Root>().DoPushRequired("hello");
			await context.WaitTransition();

			var target = context.Manager.FindState<Target>();
			Assert.AreEqual("hello", target.Text);
			Assert.AreEqual("initial-payload", target.Payload);
			Assert.AreEqual(3, target.Retry);
		}

		[Test]
		public async Task DefaultValuesCanBePassedExplicitlyViaOption()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			// Option<T> により「未指定」と「既定値を明示的に指定」が区別される。
			// null や 0 をそのまま渡せる。
			context.Manager.FindState<Root>().DoPushWithOptions("hello", null, 0);
			await context.WaitTransition();

			var target = context.Manager.FindState<Target>();
			Assert.AreEqual("hello", target.Text);
			Assert.IsNull(target.Payload, "null を明示的に渡せる");
			Assert.AreEqual(0, target.Retry, "0 を明示的に渡せる");
		}

		[Test]
		public async Task OptionalArgumentsAreAppliedWhenNonDefault()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			context.Manager.FindState<Root>().DoPushWithOptions("hello", "given", 7);
			await context.WaitTransition();

			var target = context.Manager.FindState<Target>();
			Assert.AreEqual("given", target.Payload);
			Assert.AreEqual(7, target.Retry);
		}
	}
}
