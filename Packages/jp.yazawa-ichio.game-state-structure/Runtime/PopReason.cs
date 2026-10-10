namespace GameStateStructure
{
	/// <summary>ステートが破棄された理由。</summary>
	public enum PopReason
	{
		Pop,
		GoTo,
		Replace,
		ProcessEnd,
		ParentExit,
	}
}