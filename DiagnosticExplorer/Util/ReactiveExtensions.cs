using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Text;

namespace DiagnosticExplorer.Util;

internal static class ReactiveExtensions
{

    public static IObservable<IList<TSource>> BufferWhenAvailable<TSource>
        (this IObservable<TSource> source, TimeSpan threshold)
    {
        return source.Publish(sp =>
            sp.GroupByUntil(_ => true, x => Observable.Timer(threshold))
                .SelectMany(i => i.ToList()));
    }
}