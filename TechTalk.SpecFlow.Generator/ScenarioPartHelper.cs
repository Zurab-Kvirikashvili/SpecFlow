﻿using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Gherkin.Ast;
using TechTalk.SpecFlow.Generator.Generation;
using TechTalk.SpecFlow.Parser;

namespace TechTalk.SpecFlow.Generator
{
    public class ScenarioPartHelper
    {
        private readonly LinePragmaHandler _linePragmaHandler;
        private int _tableCounter;


        public ScenarioPartHelper(LinePragmaHandler linePragmaHandler)
        {
            _linePragmaHandler = linePragmaHandler;
        }

        public void SetupFeatureBackground(TestClassGenerationContext generationContext)
        {
            if (!generationContext.Feature.HasFeatureBackground())
            {
                return;
            }

            var background = generationContext.Feature.Background;

            var backgroundMethod = generationContext.FeatureBackgroundMethod;

            backgroundMethod.Attributes = MemberAttributes.Public;
            backgroundMethod.Name = GeneratorConstants.BACKGROUND_NAME;

            _linePragmaHandler.AddLineDirective(backgroundMethod.Statements, background);

            foreach (var step in background.Steps)
            {
                GenerateStep(backgroundMethod, step, null);
            }

            _linePragmaHandler.AddLineDirectiveHidden(backgroundMethod.Statements);
        }

        public void GenerateStep(CodeMemberMethod testMethod, Step gherkinStep, ParameterSubstitution paramToIdentifier)
        {
            var testRunnerField = GetTestRunnerExpression();
            var scenarioStep = AsSpecFlowStep(gherkinStep);

            //testRunner.Given("something");
            var arguments = new List<CodeExpression> {GetSubstitutedString(scenarioStep.Text, paramToIdentifier)};
            if (scenarioStep.Argument != null)
            {
                _linePragmaHandler.AddLineDirectiveHidden(testMethod.Statements);
            }

            arguments.Add(GetDocStringArgExpression(scenarioStep.Argument as DocString, paramToIdentifier));
            arguments.Add(GetTableArgExpression(scenarioStep.Argument as DataTable, testMethod.Statements, paramToIdentifier));
            arguments.Add(new CodePrimitiveExpression(scenarioStep.Keyword));

            _linePragmaHandler.AddLineDirective(testMethod.Statements, scenarioStep);
            testMethod.Statements.Add(
                new CodeMethodInvokeExpression(
                    testRunnerField,
                    scenarioStep.StepKeyword.ToString(),
                    arguments.ToArray()));
        }

        public CodeExpression GetStringArrayExpression(IEnumerable<Tag> tags)
        {
            if (!tags.Any())
            {
                return new CodeCastExpression(typeof(string[]), new CodePrimitiveExpression(null));
            }

            return new CodeArrayCreateExpression(typeof(string[]), tags.Select(tag => new CodePrimitiveExpression(tag.GetNameWithoutAt())).Cast<CodeExpression>().ToArray());
        }

        private SpecFlowStep AsSpecFlowStep(Step step)
        {
            var specFlowStep = step as SpecFlowStep;
            if (specFlowStep == null)
            {
                throw new TestGeneratorException("The step must be a SpecFlowStep.");
            }

            return specFlowStep;
        }

        private CodeExpression GetTableArgExpression(DataTable tableArg, CodeStatementCollection statements, ParameterSubstitution paramToIdentifier)
        {
            if (tableArg == null)
            {
                return new CodeCastExpression(typeof(Table), new CodePrimitiveExpression(null));
            }

            _tableCounter++;

            //TODO[Gherkin3]: remove dependency on having the first row as header
            var header = tableArg.Rows.First();
            var body = tableArg.Rows.Skip(1).ToArray();

            //Table table0 = new Table(header...);
            var tableVar = new CodeVariableReferenceExpression("table" + _tableCounter);
            statements.Add(
                new CodeVariableDeclarationStatement(typeof(Table), tableVar.VariableName,
                    new CodeObjectCreateExpression(
                        typeof(Table),
                        GetStringArrayExpression(header.Cells.Select(c => c.Value), paramToIdentifier))));

            foreach (var row in body)
            {
                //table0.AddRow(cells...);
                statements.Add(
                    new CodeMethodInvokeExpression(
                        tableVar,
                        "AddRow",
                        GetStringArrayExpression(row.Cells.Select(c => c.Value), paramToIdentifier)));
            }

            return tableVar;
        }

        private CodeExpression GetDocStringArgExpression(DocString docString, ParameterSubstitution paramToIdentifier)
        {
            return GetSubstitutedString(docString == null ? null : docString.Content, paramToIdentifier);
        }

        public CodeExpression GetTestRunnerExpression()
        {
            return new CodeVariableReferenceExpression(GeneratorConstants.TESTRUNNER_FIELD);
        }

        private CodeExpression GetStringArrayExpression(IEnumerable<string> items, ParameterSubstitution paramToIdentifier)
        {
            return new CodeArrayCreateExpression(typeof(string[]), items.Select(item => GetSubstitutedString(item, paramToIdentifier)).ToArray());
        }

        private CodeExpression GetSubstitutedString(string text, ParameterSubstitution paramToIdentifier)
        {
            if (text == null)
            {
                return new CodeCastExpression(typeof(string), new CodePrimitiveExpression(null));
            }

            if (paramToIdentifier == null)
            {
                return new CodePrimitiveExpression(text);
            }

            var paramRe = new Regex(@"\<(?<param>[^\>]+)\>");
            var formatText = text.Replace("{", "{{").Replace("}", "}}");
            var arguments = new List<string>();

            formatText = paramRe.Replace(formatText, match =>
            {
                var param = match.Groups["param"].Value;
                string id;
                if (!paramToIdentifier.TryGetIdentifier(param, out id))
                {
                    return match.Value;
                }

                var argIndex = arguments.IndexOf(id);
                if (argIndex < 0)
                {
                    argIndex = arguments.Count;
                    arguments.Add(id);
                }

                return "{" + argIndex + "}";
            });

            if (arguments.Count == 0)
            {
                return new CodePrimitiveExpression(text);
            }

            var formatArguments = new List<CodeExpression> {new CodePrimitiveExpression(formatText)};
            formatArguments.AddRange(arguments.Select(id => new CodeVariableReferenceExpression(id)));

            return new CodeMethodInvokeExpression(
                new CodeTypeReferenceExpression(typeof(string)),
                "Format",
                formatArguments.ToArray());
        }
    }
}