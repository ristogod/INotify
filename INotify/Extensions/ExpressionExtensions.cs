﻿using System;
using System.Linq.Expressions;

namespace INotify.Extensions
{
    public static class ExpressionExtensions
    {
        public static string GetName<T>(this Expression<Func<T>> property) => (property.Body as MemberExpression)?.Member.Name;

        public static string GetName<TRef, TProp>(this Expression<Func<TRef, TProp>> property) => (property.Body as MemberExpression)?.Member.Name;
    }
}