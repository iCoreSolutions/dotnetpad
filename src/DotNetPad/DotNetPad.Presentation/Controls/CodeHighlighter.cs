﻿using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Waf.DotNetPad.Domain;

namespace Waf.DotNetPad.Presentation.Controls
{
    public sealed class CodeHighlighter : IHighlighter
    {
        private readonly TaskScheduler uiTaskScheduler;
        private readonly Func<Document> getDocument;
        private readonly List<VersionedHighlightedLine> cachedLines;
        private readonly Task initialDelayTask;


        public CodeHighlighter(IDocument document, Func<Document> getDocument)
        {
            uiTaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            Document = document;
            this.getDocument = getDocument;
            cachedLines = new List<VersionedHighlightedLine>();
            initialDelayTask = CreateInitialDelayTask();
        }


        public IDocument Document { get; }

        public HighlightingColor DefaultTextColor => CodeHighlightColors.DefaultHighlightingColor;


        public event HighlightingStateChangedEventHandler HighlightingStateChanged;


        private static async Task CreateInitialDelayTask()
        {
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        }

        public HighlightedLine HighlightLine(int lineNumber)
        {
            var documentLine = Document.GetLineByNumber(lineNumber);
            var currentVersion = Document.Version;

            EnlargeList(cachedLines, lineNumber + 1);
            var cachedLine = cachedLines[lineNumber];

            if (cachedLine != null && currentVersion != null && cachedLine.Version.CompareAge(currentVersion) == 0
                && currentVersion.BelongsToSameDocumentAs(cachedLine.Version))
            {
                return cachedLine;
            }

            cachedLines[lineNumber]?.Cancel();
            var newLine = new VersionedHighlightedLine(Document, documentLine, Document.Version, cachedLine);
            cachedLines[lineNumber] = newLine;
            UpdateHighlightLineAsync(newLine, cachedLine);

            foreach (var line in cachedLines.ToArray().Reverse())
            {
                if (!line?.DocumentLine?.IsDeleted == true)
                {
                    break;
                }
                cachedLines.Remove(line);
            }

            return newLine;
        }

        private async void UpdateHighlightLineAsync(VersionedHighlightedLine line, VersionedHighlightedLine oldHighlightedLine)
        {
            try
            {
                await Task.Run(async () =>
                {
                    await initialDelayTask.ConfigureAwait(false);
                    line.CancellationToken.ThrowIfCancellationRequested();

                    var documentLine = line.DocumentLine;
                    var currentVersion = Document.Version;
                    if (line.Version == null || !currentVersion.BelongsToSameDocumentAs(line.Version) || currentVersion.CompareAge(line.Version) != 0)
                    {
                        return;
                    }
                    var spans = await GetClassifiedSpansAsync(documentLine, line.CancellationToken).ConfigureAwait(false);
                    line.CancellationToken.ThrowIfCancellationRequested();

                    await TaskHelper.Run(() =>
                    {
                        if (line.CancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        line.Sections.Clear();
                        foreach (var classifiedSpan in spans)
                        {
                            if (IsOutsideLine(documentLine, classifiedSpan.TextSpan.Start, classifiedSpan.TextSpan.Length))
                            {
                                continue;
                            }
                            line.Sections.Add(new HighlightedSection
                            {
                                Color = CodeHighlightColors.GetHighlightingColor(classifiedSpan.ClassificationType),
                                Offset = classifiedSpan.TextSpan.Start,
                                Length = classifiedSpan.TextSpan.Length
                            });
                        }
                        if (oldHighlightedLine == null || !SectionsAreEqual(line.Sections, oldHighlightedLine.Sections))
                        {
                           HighlightingStateChanged?.Invoke(documentLine.LineNumber, documentLine.LineNumber);
                        }
                    }, uiTaskScheduler).ConfigureAwait(false);
                }, line.CancellationToken);
            }
            catch (OperationCanceledException) { }
        }

        private static bool SectionsAreEqual(IList<HighlightedSection> sections, IList<HighlightedSection> otherSections)
        {
           if (sections.Count != otherSections?.Count)
              return false;
        
           for (int i = 0; i < sections.Count; i++)
           {
              var s1 = sections[i];
              var s2 = otherSections[i];
        
              if (!s1.Color.Equals(s2.Color) || s1.Length != s2.Length || s1.Offset != s2.Offset)
                 return false;
           }
        
           return true;
        }

        private static bool IsOutsideLine(IDocumentLine documentLine, int offset, int length)
        {
            return offset < documentLine.Offset || offset + length > documentLine.EndOffset;
        }

        private async Task<IEnumerable<ClassifiedSpan>> GetClassifiedSpansAsync(IDocumentLine documentLine, CancellationToken cancellationToken)
        {
            var document = getDocument();
            var text = await document.GetTextAsync().ConfigureAwait(false);
            if (text.Length >= documentLine.Offset + documentLine.TotalLength)
            {
                return await Classifier.GetClassifiedSpansAsync(document,
                    new TextSpan(documentLine.Offset, documentLine.TotalLength), cancellationToken).ConfigureAwait(false);
            }
            return Enumerable.Empty<ClassifiedSpan>();
        }

        public void BeginHighlighting()
        {
        }

        public void EndHighlighting()
        {
        }

        public HighlightingColor GetNamedColor(string name)
        {
            return null;
        }

        public IEnumerable<HighlightingColor> GetColorStack(int lineNumber)
        {
            return null;
        }

        public void UpdateHighlightingState(int lineNumber)
        {
        }

        public void Dispose()
        {
            cachedLines.Clear();
        }

        private static void EnlargeList<T>(List<T> list, int newCount)
        {
            if (newCount > list.Count)
            {
                list.AddRange(Enumerable.Repeat(default(T), newCount - list.Count));
            }
        }


        private sealed class VersionedHighlightedLine : HighlightedLine
        {
            private readonly CancellationTokenSource cancellationTokenSource;

            public VersionedHighlightedLine(IDocument document, IDocumentLine documentLine, ITextSourceVersion version, VersionedHighlightedLine oldVersion)
                : base(document, documentLine)
            {
                Version = version;
                cancellationTokenSource = new CancellationTokenSource();
                CancellationToken = cancellationTokenSource.Token;
                if (oldVersion != null)
                {
                    foreach (var oldSection in oldVersion.Sections)
                    {
                        if (IsOutsideLine(documentLine, oldSection.Offset, oldSection.Length))
                        {
                            continue;
                        }
                        Sections.Add(new HighlightedSection
                        {
                            Color = oldSection.Color,
                            Offset = oldSection.Offset,
                            Length = oldSection.Length
                        });
                    }
                }
            }

            public ITextSourceVersion Version { get; }

            public CancellationToken CancellationToken { get; }


            public void Cancel()
            {
                cancellationTokenSource.Cancel();
            }
        }
    }
}
