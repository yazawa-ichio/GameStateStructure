using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace GameStateStructure.Generator
{
	class SyntaxReceiver : ISyntaxReceiver
	{
		public List<TypeDeclarationSyntax> List { get; } = new();

		/// <summary>[SubscribeEvent] が付いたメソッド。クラス側の属性とは独立に収集する。</summary>
		public List<MethodDeclarationSyntax> SubscribeEventMethods { get; } = new();

		public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
		{
			if (syntaxNode is MethodDeclarationSyntax methodDeclarationSyntax)
			{
				var isSubscribe = methodDeclarationSyntax.AttributeLists
					.SelectMany(attributeList => attributeList.Attributes)
					.Any(x => IsSubscribeEvent(x));
				if (isSubscribe)
				{
					SubscribeEventMethods.Add(methodDeclarationSyntax);
				}
			}

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

			bool IsSubscribeEvent(AttributeSyntax attribute)
			{
				switch (attribute.Name.ToFullString().Trim())
				{
					case "GameStateStructure.SubscribeEvent":
					case "SubscribeEvent":
					case "GameStateStructure.SubscribeEventAttribute":
					case "SubscribeEventAttribute":
						return true;
					default:
						return false;
				}
			}

			bool IsTarget(AttributeSyntax attribute)
			{
				switch (attribute.Name.ToFullString().Trim())
				{
					case "GameStateStructure.GoTo":
					case "GoTo":
					case "GameStateStructure.GoToAttribute":
					case "GoToAttribute":

					case "GameStateStructure.Push":
					case "Push":
					case "GameStateStructure.PushAttribute":
					case "PushAttribute":

					case "GameStateStructure.PublishEventAttribute":
					case "PublishEvent":
					case "GameStateStructure.PublishEvent":
					case "PublishEventAttribute":
						return true;
					default:
						return false;
				}
			}
		}
	}

}