using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace GameStateStructure.Generator
{
	class SyntaxReceiver : ISyntaxReceiver
	{
		public List<TypeDeclarationSyntax> List { get; } = new();

		public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
		{
			if (syntaxNode is ClassDeclarationSyntax classDeclarationSyntax)
			{
				var isTarget = classDeclarationSyntax.AttributeLists
					.SelectMany(attributeList => attributeList.Attributes)
					.Any(x => IsTarget(x));
				if (isTarget)
				{
					List.Add(classDeclarationSyntax);
				}
			}

			bool IsTarget(AttributeSyntax attribute)
			{
				switch (attribute.Name.ToFullString().Trim())
				{
					case "GameStateStructure.ChangeTo":
					case "ChangeTo":
					case "GameStateStructure.ChangeToAttribute":
					case "ChangeToAttribute":
					case "GameStateStructure.Push":
					case "Push":
					case "GameStateStructure.PushAttribute":
					case "PushAttribute":
						return true;
					default:
						return false;
				}
			}
		}
	}

}