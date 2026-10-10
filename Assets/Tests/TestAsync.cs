using System;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	static class TestAsync
	{
		/// <summary>
		/// Unity の PlayMode テストは async Task をコルーチンで 1 フレームずつ進めるため、
		/// Assert.ThrowsAsync のようにメインスレッドを同期待ちする API を使うと、
		/// 待ち先の継続がポンプされずデッドロックする。
		/// （既に完了しているタスクに対してだけは Assert.ThrowsAsync も動くが、
		/// 判別が難しいので例外の検証は常にこのヘルパ経由で await する。）
		/// <para>
		/// さらに IL2CPP + マネージドストリッピングでは、Assert.ThrowsAsync の内部が
		/// リフレクションで Task を待つため NullReferenceException になる。
		/// この点でも Assert.ThrowsAsync は使えない。
		/// </para>
		/// </summary>
		public static async Task<Exception> Catch(Func<Task> action)
		{
			try
			{
				await action();
				return null;
			}
			catch (Exception e)
			{
				return e;
			}
		}
	}
}
