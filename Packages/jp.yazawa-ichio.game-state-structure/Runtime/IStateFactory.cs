using System;

namespace GameStateStructure
{
	/// <summary>ステートを生成するときの文脈。</summary>
	public readonly struct StateCreationContext
	{
		/// <summary>入る車線。主軸なら null。</summary>
		public Type Branch { get; }

		public StateCreationContext(Type branch)
		{
			Branch = branch;
		}
	}

	/// <summary>
	/// ステートの生成を差し替える。DI コンテナとの接合に使う。
	/// <para>
	/// <b>返す値は必ずファクトリが所有するものにすること。</b>
	/// 親から受け取ったものをそのまま返すと、子の終了時に親のものが破棄される。
	/// </para>
	/// </summary>
	public interface IStateFactory
	{
		(T State, IDisposable Value) Create<T>(
			in StateCreationContext context,
			IDisposable parent) where T : GameState;
	}

	/// <summary>IStateFactory の型付き版。</summary>
	public abstract class StateFactory<TValue> : IStateFactory where TValue : class, IDisposable
	{
		(T State, IDisposable Value) IStateFactory.Create<T>(
			in StateCreationContext context,
			IDisposable parent)
		{
			var (state, value) = Create<T>(context, parent as TValue);
			return (state, value);
		}

		protected abstract (T State, TValue Value) Create<T>(
			in StateCreationContext context,
			TValue parent) where T : GameState;
	}
}