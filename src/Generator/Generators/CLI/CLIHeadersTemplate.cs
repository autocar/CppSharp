﻿using System;
using System.Collections.Generic;
using System.IO;

namespace Cxxi.Generators.CLI
{
    public class CLIHeadersTemplate : CLITextTemplate
    {
        public override string FileExtension { get { return "h"; } }

        private CLIForwardRefeferencePrinter forwardRefsPrinter;

        protected override void Generate()
        {
            GenerateStart();

            WriteLine("#pragma once");
            NewLine();

            WriteLine("#include <{0}>", Module.IncludePath);
            GenerateIncludeForwardRefs();

            NewLine();

            WriteLine("namespace {0}", SafeIdentifier(Library.Name));
            WriteLine("{");
            GenerateDeclarations();
            WriteLine("}");
        }

        public void GenerateIncludeForwardRefs()
        {
            forwardRefsPrinter = new CLIForwardRefeferencePrinter();

            foreach (var forwardRef in Module.ForwardReferences)
                forwardRef.Visit(forwardRefsPrinter);

            var includes = new HashSet<string>();

            foreach (var include in forwardRefsPrinter.Includes)
            {
                if (string.IsNullOrWhiteSpace(include))
                    continue;

                if (include == Path.GetFileNameWithoutExtension(Module.FileName))
                    continue;

                includes.Add(string.Format("#include \"{0}.h\"", include));
            }

            foreach (var include in includes)
                WriteLine(include);
        }

        public void GenerateForwardRefs()
        {
            // Use a set to remove duplicate entries.
            var forwardRefs = new HashSet<string>();

            foreach (var forwardRef in forwardRefsPrinter.Refs)
            {
                forwardRefs.Add(forwardRef);
            }

            foreach (var forwardRef in forwardRefs)
            {
                WriteLine(forwardRef);
            }

            if (forwardRefs.Count > 0)
                NewLine();
        }

        public void GenerateDeclarations()
        {
            PushIndent();

            // Generate the forward references.
            GenerateForwardRefs();

            bool needsNewline = false;

            // Generate all the enum declarations for the module.
            for (int i = 0; i < Module.Enums.Count; ++i)
            {
                var @enum = Module.Enums[i];

                if (@enum.Ignore || @enum.IsIncomplete)
                    continue;

                GenerateEnum(@enum);
                needsNewline = true;
                if (i < Module.Enums.Count - 1)
                    NewLine();
            }

            if (needsNewline)
                NewLine();

            needsNewline = false;

            // Generate all the typedef declarations for the module.
            foreach (var typedef in Module.Typedefs)
            {
                if (typedef.Ignore)
                    continue;

                if (!GenerateTypedef(typedef))
                    continue;

                NewLine();
            }

            needsNewline = false;

            // Generate all the struct/class declarations for the module.
            for (var i = 0; i < Module.Classes.Count; ++i)
            {
                var @class = Module.Classes[i];

                if (@class.Ignore || @class.IsIncomplete)
                    continue;

                if (@class.IsOpaque)
                    continue;

                GenerateClass(@class);
                needsNewline = true;

                if (i < Module.Classes.Count - 1)
                    NewLine();
            }

            if (Module.HasFunctions)
            {
                if (needsNewline)
                    NewLine();

                WriteLine("public ref class {0}{1}", SafeIdentifier(Library.Name),
                    Module.FileNameWithoutExtension);
                WriteLine("{");
                WriteLine("public:");
                PushIndent();

                // Generate all the function declarations for the module.
                foreach (var function in Module.Functions)
                {
                    GenerateFunction(function);
                }

                PopIndent();
                WriteLine("};");
            }

            PopIndent();
        }

        public void GenerateDeclarationCommon(Declaration T)
        {
            GenerateSummary(T.BriefComment);
            GenerateDebug(T);
        }

        public void GenerateClass(Class @class)
        {
            if (@class.Ignore || @class.IsIncomplete)
                return;

            GenerateDeclarationCommon(@class);

            if (@class.IsUnion)
            {
                // TODO: How to do wrapping of unions?
                //const string @namespace = "System::Runtime::InteropServices";
                //WriteLine("[{0}::StructLayout({0}::LayoutKind::Explicit)]",
                //    @namespace);
                Console.WriteLine("Unions are not yet implemented");
            }

            Write("public ");

            if (@class.IsValueType)
                Write("value struct ");
            else
                Write("ref class ");

            Write("{0}", SafeIdentifier(@class.Name));

            if (@class.IsOpaque)
            {
                WriteLine(";");
                return;
            }

            if (@class.HasBase)
                Write(" : {0}", SafeIdentifier(@class.Bases[0].Class.Name));

            WriteLine(string.Empty);
            WriteLine("{");
            WriteLine("public:");

            var nativeType = string.Format("::{0}*", @class.QualifiedOriginalName);

            if (@class.IsRefType)
            {
                PushIndent();
                WriteLine("property {0} NativePtr;", nativeType);
                PopIndent();
                NewLine();
            }

            // Output a default constructor that takes the native pointer.
            PushIndent();
            WriteLine("{0}({1} native);", SafeIdentifier(@class.Name), nativeType);
            WriteLine("{0}({1} native);", SafeIdentifier(@class.Name), "System::IntPtr");
            PopIndent();

            if (@class.IsValueType)
            {
                PushIndent();
                foreach(var field in @class.Fields)
                {
                    if (field.Ignore) continue;

                    GenerateDeclarationCommon(field);
                    if (@class.IsUnion)
                        WriteLine("[FieldOffset({0})]", field.Offset);
                    WriteLine("{0} {1};", field.Type, SafeIdentifier(field.Name));
                }
                PopIndent();
            }

            // Generate a property for each field if class is not value type
            if (@class.IsRefType)
            {
                PushIndent();
                foreach (var field in @class.Fields)
                {
                    if (CheckIgnoreField(@class, field))
                        continue;

                    GenerateDeclarationCommon(field);
                    GenerateFieldProperty(field);
                }
                PopIndent();
            }

            PushIndent();
            foreach (var method in @class.Methods)
            {
                if (CheckIgnoreMethod(@class, method))
                    continue;

                GenerateDeclarationCommon(method);
                GenerateMethod(method);
            }
            PopIndent();

            WriteLine("};");
        }

        public void GenerateFieldProperty(Field field)
        {
            field.Type.Visit<string>(Type.TypePrinter);

            var type = field.Type.Visit(Type.TypePrinter);

            WriteLine("property {0} {1};", type, field.Name);
            NewLine();
        }

        public void GenerateMethod(Method method)
        {
            if (method.Ignore) return;

            if (method.Access != AccessSpecifier.Public)
                return;

            GenerateDeclarationCommon(method);

            if (method.Kind == CXXMethodKind.Constructor || method.Kind == CXXMethodKind.Destructor)
                Write("{0}(", SafeIdentifier(method.Name));
            else
                Write("{0} {1}(", method.ReturnType, SafeIdentifier(method.Name));

            for (var i = 0; i < method.Parameters.Count; ++i)
            {
                var param = method.Parameters[i];
                Write("{0}", TypeSig.GetArgumentString(param));
                if (i < method.Parameters.Count - 1)
                    Write(", ");
            }

            WriteLine(");");
        }

        public bool GenerateTypedef(TypedefDecl typedef)
        {
            if (typedef.Ignore)
                return false;

            GenerateDeclarationCommon(typedef);

            FunctionType function;
            if (typedef.Type.IsPointerTo<FunctionType>(out function))
            {
                WriteLine("public {0};",
                    string.Format(TypeSig.ToDelegateString(function),
                    SafeIdentifier(typedef.Name)));
                return true;
            }
            else if (typedef.Type.IsEnumType())
            {
                // Already handled in the parser.
            }
            else
            {
                Console.WriteLine("Unhandled typedef type: {0}", typedef);
            }

            return false;
        }

        public void GenerateFunction(Function function)
        {
            if (function.Ignore) return;
            GenerateDeclarationCommon(function);

            Write("static {0} {1}(", function.ReturnType, SafeIdentifier(function.Name));

            for (int i = 0; i < function.Parameters.Count; ++i)
            {
                var param = function.Parameters[i];
                Write("{0}", TypeSig.GetArgumentString(param));
                if (i < function.Parameters.Count - 1)
                    Write(", ");
            }

            WriteLine(");");
        }

        public void GenerateDebug(Declaration decl)
        {
            if (Options.OutputDebug && !String.IsNullOrWhiteSpace(decl.DebugText))
                WriteLine("// DEBUG: " + decl.DebugText);
        }

        public void GenerateEnum(Enumeration @enum)
        {
            if (@enum.Ignore || @enum.IsIncomplete)
                return;

            GenerateDeclarationCommon(@enum);

            if (@enum.Modifiers.HasFlag(Enumeration.EnumModifiers.Flags))
                WriteLine("[System::Flags]");

            Write("public enum struct {0}", SafeIdentifier(@enum.Name));

            if (@enum.BuiltinType.Type != PrimitiveType.Int32)
                WriteLine(" : {0}", TypeSig.VisitPrimitiveType(@enum.BuiltinType.Type));
            else
                NewLine();

            WriteLine("{");

            PushIndent();
            for (int i = 0; i < @enum.Items.Count; ++i)
            {
                var I = @enum.Items[i];
                GenerateInlineSummary(I.Comment);
                if (I.ExplicitValue)
                    Write(String.Format("{0} = {1}", SafeIdentifier(I.Name), I.Value));
                else
                    Write(String.Format("{0}", SafeIdentifier(I.Name)));

                if (i < @enum.Items.Count - 1)
                    WriteLine(",");
            }
            PopIndent();
            NewLine();
            WriteLine("};");
        }
    }
}
