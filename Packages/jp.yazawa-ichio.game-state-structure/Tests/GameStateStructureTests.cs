using NUnit.Framework;
using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameStateStructure.Tests
{
	public partial class GameStateStructureTests
	{
		class Context : IDisposable
		{
			GameObject Owner { get; }

			public GameStateManager Manager { get; }

			public Context()
			{
				Owner = new GameObject("Context");
				Manager = Owner.AddComponent<GameStateManager>();
			}

			public void Dispose()
			{
				GameObject.Destroy(Owner);
			}

		}

		IEnumerator Async(Func<Task> task)
		{
			var t = task();
			while (!t.IsCompleted)
			{
				yield return null;
			}
			if (t.IsFaulted)
			{
				Assert.Fail(t.Exception.ToString());
			}
		}

		[ChangeTo(typeof(Root))]
		partial class Root : GameState
		{
		}

		[UnityTest]
		public IEnumerator Tests() => Async(async () =>
		{
			using (var context = new Context())
			{
				await context.Manager.Entry<Root>();
			}
		});

	}
}
