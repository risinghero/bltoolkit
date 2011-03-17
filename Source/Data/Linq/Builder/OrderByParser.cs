﻿using System;
using System.Linq;
using System.Linq.Expressions;

namespace BLToolkit.Data.Linq.Parser
{
	using BLToolkit.Linq;
	using Data.Sql;

	class OrderByParser : MethodCallParser
	{
		protected override bool CanParseMethodCall(ExpressionParser parser, MethodCallExpression methodCall, ParseInfo parseInfo)
		{
			if (!methodCall.IsQueryable("OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending"))
				return false;

			var body = ((LambdaExpression)methodCall.Arguments[1].Unwrap()).Body.Unwrap();

			if (body.NodeType == ExpressionType	.MemberInit)
			{
				var mi = (MemberInitExpression)body;
				bool throwExpr;

				if (mi.NewExpression.Arguments.Count > 0 || mi.Bindings.Count == 0)
					throwExpr = true;
				else
					throwExpr = mi.Bindings.Any(b => b.BindingType != MemberBindingType.Assignment);

				if (throwExpr)
					throw new NotSupportedException(string.Format("Explicit construction of entity type '{0}' in order by is not allowed.", body.Type));
			}

			return true;
		}

		protected override IParseContext ParseMethodCall(ExpressionParser parser, MethodCallExpression methodCall, ParseInfo parseInfo)
		{
			var sequence = parser.ParseSequence(new ParseInfo(parseInfo, methodCall.Arguments[0]));

			if (sequence.SqlQuery.Select.TakeValue != null || sequence.SqlQuery.Select.SkipValue != null)
				sequence = new SubQueryContext(sequence);

			var lambda  = (LambdaExpression)methodCall.Arguments[1].Unwrap();
			var sparent = sequence.Parent;
			var order   = new ExpressionContext(parseInfo.Parent, sequence, lambda);
			var body    = lambda.Body.Unwrap();
			var sql     = parser.ParseExpressions(order, body, ConvertFlags.Key);

			sequence.Parent = sparent;

			if (!methodCall.Method.Name.StartsWith("Then"))
				sequence.SqlQuery.OrderBy.Items.Clear();

			foreach (var expr in sql)
			{
				var e = parser.ConvertSearchCondition(sequence, expr.Sql);
				sequence.SqlQuery.OrderBy.Expr(e, methodCall.Method.Name.EndsWith("Descending"));
			}

			return sequence;
		}
	}
}