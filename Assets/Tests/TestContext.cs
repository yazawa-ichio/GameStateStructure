using System;
using UnityEngine;

namespace GameStateStructure.Tests
{
	class TestContext : IDisposable
	{
		GameObject Owner { get; }

		public GameStateManager Manager { get; }

		public TestContext()
		{
			Owner = new GameObject("Context");
			Manager = Owner.AddComponent<GameStateManager>();
			Manager.ErrorHandler = new TestErrorHandler();
		}

		public void Dispose()
		{
			GameObject.Destroy(Owner);
		}

	}


}
