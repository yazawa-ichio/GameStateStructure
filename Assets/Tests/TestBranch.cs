using System.Threading;
using System.Threading.Tasks;

namespace GameStateStructure.Tests
{
	/// <summary>テストで使う車線。方針は既定の Replace。</summary>
	class TestBranch : Branch
	{
	}

	/// <summary>占有中の要求を拒否する車線。</summary>
	class RejectBranch : Branch
	{
		public override BranchOccupied Occupied => BranchOccupied.Reject;
	}

	/// <summary>
	/// 車線を占有し続けるステート。<see cref="Finish"/> を呼ぶか、
	/// 親の破棄でキャンセルされるまで Run は返らない。
	/// <para>
	/// 継続がキャンセル元のスレッドで同期実行されると解体経路に割り込むため、
	/// RunContinuationsAsynchronously を指定している。
	/// </para>
	/// </summary>
	abstract class ResidentProcess : GameState, IProcess
	{
		readonly TaskCompletionSource<bool> m_Completion =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task Run(CancellationToken ct)
		{
			ct.Register(() => m_Completion.TrySetCanceled());
			return m_Completion.Task;
		}

		public void Finish() => m_Completion.TrySetResult(true);
	}
}
