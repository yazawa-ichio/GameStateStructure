using System;
using System.Threading.Tasks;
using UnityEngine;

namespace GameStateStructure.Tests
{
	class TestContext : IDisposable
	{
		GameObject Owner { get; }

		public GameStateManager Manager { get; }

		public TestErrorHandler ErrorHandler { get; }

		public Exception LastError => ErrorHandler.Error;

		public TestContext()
		{
			Owner = new GameObject("Context");
			Manager = Owner.AddComponent<GameStateManager>();
			ErrorHandler = new TestErrorHandler();
			Manager.ErrorHandler = ErrorHandler;
		}

		/// <summary>
		/// Entry は Config の内容で ErrorHandler / Activator / Decorators を上書きするため、
		/// 素の Manager.Entry を呼ぶと TestContext が設定したハンドラが失われる。
		/// このヘルパ経由なら常にハンドラが載った状態になる。
		/// </summary>
		public Task Entry<T>() where T : GameState
		{
			return Entry<T>(new ParameterHolder());
		}

		public Task Entry<T>(ParameterHolder parameter) where T : GameState
		{
			return Manager.Entry<T>(parameter, CreateConfig());
		}

		public Config CreateConfig()
		{
			return new Config { ErrorHandler = ErrorHandler };
		}

		/// <summary>
		/// 遷移は async void で走るため、完了を待つ必要がある場合に使う。
		/// AsyncLock を保持している間は IsBusy が立つ。
		/// </summary>
		public async Task WaitTransition(float timeoutSeconds = 5f)
		{
			var limit = Time.realtimeSinceStartup + timeoutSeconds;
			while (Manager.IsBusy)
			{
				await Task.Yield();
				if (Time.realtimeSinceStartup > limit)
				{
					throw new TimeoutException("transition did not complete");
				}
			}
		}

		/// <summary>
		/// フレームを指定回数進める。OnUpdate の検証などに使う。
		/// </summary>
		public async Task Tick(int count = 1)
		{
			for (var i = 0; i < count; i++)
			{
				await Task.Yield();
			}
		}

		public void Dispose()
		{
			GameObject.Destroy(Owner);
		}

	}


}
