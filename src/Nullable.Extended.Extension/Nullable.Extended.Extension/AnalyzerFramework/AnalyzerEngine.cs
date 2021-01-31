﻿using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using TomsToolbox.Composition;

namespace Nullable.Extended.Extension.AnalyzerFramework
{
    [Export(typeof(IAnalyzerEngine))]
    class AnalyzerEngine : IAnalyzerEngine
    {
        private readonly IEnumerable<ISyntaxTreeAnalyzer> _syntaxTreeAnalyzers;
        private readonly IEnumerable<ISyntaxAnalysisPostProcessor> _postProcessors;

        public AnalyzerEngine(IExportProvider exportProvider)
        {
            _syntaxTreeAnalyzers = exportProvider.GetExportedValues<ISyntaxTreeAnalyzer>().ToImmutableArray();
            _postProcessors = exportProvider.GetExportedValues<ISyntaxAnalysisPostProcessor>().ToImmutableArray();
        }

        public async Task<IEnumerable<AnalysisResult>> AnalyzeAsync(IEnumerable<Document> documents)
        {
            var documentsByProject = documents.GroupBy(document => document.Project);

            var tasks = documentsByProject.Select(AnalyzeAsync).ToImmutableArray();
            
            var results = await Task.WhenAll(tasks);

            return results.SelectMany(r => r).ToImmutableArray();
        }

        private async Task<IEnumerable<AnalysisResult>> AnalyzeAsync(IGrouping<Project, Document> documents)
        {
            var project = documents.Key;

            var tasks = documents.Select(AnalyzeAsync).ToImmutableArray();

            IEnumerable<AnalysisResult> analysisResults = (await Task.WhenAll(tasks))
                .SelectMany(r => r)
                .ToList();

            foreach (var analyzer in _postProcessors)
            {
                analysisResults = await analyzer.PostProcessAsync(project, analysisResults);
            }

            return analysisResults;
        }

        private Task<IEnumerable<AnalysisResult>> AnalyzeAsync(Document document)
        {
            return Task.Run(async () =>
            {
                var syntaxTree = await document.GetSyntaxTreeAsync();
                if (syntaxTree == null)
                    return Enumerable.Empty<AnalysisResult>();

                var syntaxRoot = await syntaxTree.GetRootAsync();
                if (syntaxRoot.BeginsWithAutoGeneratedComment())
                    return Enumerable.Empty<AnalysisResult>();

                var tasks = _syntaxTreeAnalyzers
                    .Select(analyzer => analyzer.AnalyzeAsync(new AnalysisContext(document, syntaxTree, syntaxRoot)))
                    .ToImmutableArray();

                var results = await Task.WhenAll(tasks);

                return results.SelectMany(r => r);
            });
        }
    }
}