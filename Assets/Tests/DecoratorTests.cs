using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>
	/// Decorator の検証。調査時点でこの機能はテストが 1 件も無かった。
	/// </summary>
	public partial class DecoratorTests
	{
		static List<string> s_Log;

		[SetUp]
		public void SetUp() => s_Log = new List<string>();

		static string Joined => string.Join(",", s_Log);

		[AttributeUsage(AttributeTargets.Class)]
		class FadeAttribute : Attribute { }

		interface IMarked { }

		/// <summary>Attribute をマーカーにする系統。</summary>
		class AttributeDecorator : Decorator<FadeAttribute>
		{
			readonly string m_Name;
			readonly int m_Priority;

			public AttributeDecorator(string name, int priority = 0)
			{
				m_Name = name;
				m_Priority = priority;
			}

			public override int Priority => m_Priority;

			protected override Task OnPreInitialize(GameState state, FadeAttribute maker)
			{
				Record(state, "PreInitialize");
				return Task.CompletedTask;
			}

			protected override Task OnPostInitialize(GameState state, FadeAttribute maker)
			{
				Record(state, "PostInitialize");
				return Task.CompletedTask;
			}

			protected override Task OnPreEnter(GameState state, FadeAttribute maker)
			{
				Record(state, "PreEnter");
				return Task.CompletedTask;
			}

			protected override void OnPostEnter(GameState state, FadeAttribute maker)
			{
				Record(state, "PostEnter");
			}

			protected override Task OnPreExit(GameState state, FadeAttribute maker)
			{
				Record(state, "PreExit");
				return Task.CompletedTask;
			}

			protected override Task OnPostExit(GameState state, FadeAttribute maker)
			{
				Record(state, "PostExit");
				return Task.CompletedTask;
			}

			void Record(GameState state, string hook) => s_Log.Add($"{m_Name}.{hook}");
		}

		/// <summary>解放経路の検証用。</summary>
		class ReleaseProbeDecorator : Decorator<FadeAttribute>
		{
			public bool Forced { get; private set; }

			protected override void OnForceRelease()
			{
				Forced = true;
				s_Log.Add("ForceRelease");
			}
		}

		/// <summary>インターフェースをマーカーにする系統。</summary>
		class InterfaceDecorator : Decorator<IMarked>
		{
			protected override void OnPostEnter(GameState state, IMarked maker)
			{
				s_Log.Add($"Interface.PostEnter({state.GetType().Name})");
			}
		}

		[Push(typeof(Marked), "Marked")]
		[Push(typeof(Plain), "Plain")]
		[Push(typeof(InterfaceMarked), "InterfaceMarked")]
		partial class Root : GameState
		{
			public DecoratorCollection OwnDecorators => DescendantDecorators;

			public void DoPushMarked() => PushMarked<Marked>();

			public void DoPushPlain() => PushPlain<Plain>();

			public void DoPushInterfaceMarked() => PushInterfaceMarked<InterfaceMarked>();
		}

		[Fade]
		class Marked : GameState
		{
			public DecoratorCollection OwnDecorators => DescendantDecorators;

			protected override Task OnInitialize()
			{
				s_Log.Add("State.Initialize");
				return Task.CompletedTask;
			}

			protected override Task OnPreEnter()
			{
				s_Log.Add("State.PreEnter");
				return Task.CompletedTask;
			}

			protected override void OnEnter() => s_Log.Add("State.Enter");

			protected override Task OnPreExit()
			{
				s_Log.Add("State.PreExit");
				return Task.CompletedTask;
			}

			protected override Task OnExit()
			{
				s_Log.Add("State.Exit");
				return Task.CompletedTask;
			}

			public void DoPop() => Pop();
		}

		class Plain : GameState
		{
		}

		class InterfaceMarked : GameState, IMarked
		{
		}

		Task EntryWith(TestContext context, params IDecorator[] decorators)
		{
			var config = context.CreateConfig();
			foreach (var decorator in decorators)
			{
				config.Decorator.Register(decorator);
			}
			return context.Manager.Entry<Root>(new ParameterHolder(), config);
		}

		[Test]
		public async Task HooksInterleaveWithStateLifecycle()
		{
			using var context = new TestContext();
			await EntryWith(context, new AttributeDecorator("Dec"));
			s_Log.Clear();

			context.Manager.FindState<Root>().DoPushMarked();
			await context.WaitTransition();

			Assert.AreEqual(
				"Dec.PreInitialize,State.Initialize,Dec.PostInitialize," +
				"State.PreEnter,Dec.PreEnter,State.Enter,Dec.PostEnter",
				Joined);
		}

		[Test]
		public async Task ExitHooksWrapStateExit()
		{
			using var context = new TestContext();
			await EntryWith(context, new AttributeDecorator("Dec"));
			context.Manager.FindState<Root>().DoPushMarked();
			await context.WaitTransition();
			s_Log.Clear();

			context.Manager.FindState<Marked>().DoPop();
			await context.WaitTransition();

			Assert.AreEqual("Dec.PreExit,State.PreExit,State.Exit,Dec.PostExit", Joined);
		}

		[Test]
		public async Task DecoratorSkipsStatesWithoutTheMarker()
		{
			using var context = new TestContext();
			await EntryWith(context, new AttributeDecorator("Dec"));
			s_Log.Clear();

			context.Manager.FindState<Root>().DoPushPlain();
			await context.WaitTransition();

			Assert.IsEmpty(s_Log, "マーカーの無いステートでは 1 度も呼ばれない");
		}

		[Test]
		public async Task InterfaceMarkerIsSupported()
		{
			using var context = new TestContext();
			await EntryWith(context, new InterfaceDecorator());
			s_Log.Clear();

			context.Manager.FindState<Root>().DoPushInterfaceMarked();
			await context.WaitTransition();

			Assert.AreEqual("Interface.PostEnter(InterfaceMarked)", Joined);
		}

		[Test]
		public async Task DecoratorsRunInPriorityOrder()
		{
			using var context = new TestContext();
			await EntryWith(context,
				new AttributeDecorator("High", 10),
				new AttributeDecorator("Low", 1));
			s_Log.Clear();

			context.Manager.FindState<Root>().DoPushMarked();
			await context.WaitTransition();

			// Priority の昇順で実行される
			Assert.AreEqual("Low.PreInitialize", s_Log[0]);
			Assert.AreEqual("High.PreInitialize", s_Log[1]);
		}

		[Test]
		public async Task UnregisterRunsForceRelease()
		{
			using var context = new TestContext();
			var decorator = new ReleaseProbeDecorator();
			await EntryWith(context, decorator);
			s_Log.Clear();

			context.Manager.Decorators.Unregister(decorator);

			// 以前は IDisposable.Dispose が空実装で、解放処理が一切呼ばれなかった
			Assert.IsTrue(decorator.Forced);
			CollectionAssert.AreEqual(new[] { "ForceRelease" }, s_Log);
		}

		[Test]
		public async Task ReEntryRunsForceReleaseSynchronously()
		{
			using var context = new TestContext();
			var decorator = new ReleaseProbeDecorator();
			await EntryWith(context, decorator);
			s_Log.Clear();

			await context.Entry<Root>();

			// Entry の再入は待たない経路に統一されている
			Assert.IsTrue(decorator.Forced);
			CollectionAssert.AreEqual(new[] { "ForceRelease" }, s_Log);
		}

		[Test]
		public async Task AncestorDecoratorsApplyToDescendants()
		{
			using var context = new TestContext();
			await context.Entry<Root>();

			// 親ステートに登録した Decorator が子に効く
			context.Manager.FindState<Root>().OwnDecorators.Register(new AttributeDecorator("FromRoot"));
			s_Log.Clear();

			context.Manager.FindState<Root>().DoPushMarked();
			await context.WaitTransition();

			CollectionAssert.Contains(s_Log, "FromRoot.PostEnter");
		}

		[Test]
		public async Task StateOwnDecoratorsDoNotApplyToItself()
		{
			using var context = new TestContext();
			await context.Entry<Root>();
			context.Manager.FindState<Root>().DoPushMarked();
			await context.WaitTransition();

			var marked = context.Manager.FindState<Marked>();
			marked.OwnDecorators.Register(new AttributeDecorator("Self"));
			s_Log.Clear();

			// 自分自身の Decorators は自分には適用されない（子孫にのみ効く）
			marked.DoPop();
			await context.WaitTransition();

			CollectionAssert.DoesNotContain(s_Log, "Self.PreExit");
		}
	}
}
