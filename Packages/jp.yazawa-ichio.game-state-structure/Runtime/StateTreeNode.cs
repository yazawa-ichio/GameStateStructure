namespace GameStateStructure
{
	/// <summary>ツリーを平坦化したときの 1 要素。</summary>
	public readonly struct StateTreeNode
	{
		public GameState State { get; }

		/// <summary>ルートからの深さ。ルートは 0。</summary>
		public int Depth { get; }

		/// <summary>入っている車線。主軸なら null。</summary>
		public System.Type Branch { get; }

		public StateTreeNode(GameState state, int depth, System.Type branch = null)
		{
			State = state;
			Depth = depth;
			Branch = branch;
		}

		public override string ToString()
		{
			var branch = Branch == null ? string.Empty : $"[{Branch.Name}] ";
			return $"{new string(' ', Depth * 2)}{branch}{State?.GetType().Name}";
		}
	}
}