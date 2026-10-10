using System.Threading;
using System.Threading.Tasks;

namespace GameStateStructure
{
	/// <summary>
	/// ステート終了時に実行される非同期処理。
	/// <para>
	/// <b>両方が呼ばれることがある。</b><c>Run</c> の完了を待っている間に強制解放が来ると、
	/// 待ちを打ち切るために <c>ForceRelease</c> も呼ぶ。
	/// <c>ForceRelease</c> は <c>Run</c> が走っている途中でも安全に書くこと。
	/// </para>
	/// </summary>
	public interface IExitTask
	{
		Task Run(CancellationToken ct);

		void ForceRelease();
	}
}