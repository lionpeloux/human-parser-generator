// Given a Parser Model, the Emitter generates CSharp code
// author: Christophe VG <contact@christophe.vg>

using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

using HumanParserGenerator;

namespace HumanParserGenerator.Emitter {
  
  public class CSharp {
    
    public bool         EmitInfo  { get; set; }
    public List<string> Sources   { get; set; }
    public string       Namespace { get; set; }

    private Generator.Model Model;

    public CSharp Generate(Generator.Model model) {
      this.Model = model;
      return this;
    }

    public override string ToString() {
      if( this.Model == null ) { return "// no model generated"; }
      if( this.Model.Entities.Count == 0) { return "// no entities generated"; }
      return string.Join("\n\n", 
        new List<string>() { 
          this.GenerateHeader(),
          this.GenerateReferences(),
          this.GenerateNamespace(),
          this.GenerateEntities(),
          this.GenerateParsers(),
          this.GenerateExtracting(),
          this.GenerateFooter()
        }.Where(x => x != null)
      );
    }

    private string GenerateHeader() {
      string header = null;
      if( this.EmitInfo ) {
        header += @"// DO NOT EDIT THIS FILE
// This file was generated using the Human Parser Generator
// (https://github.com/christophevg/human-parser-generator)
// on " + DateTime.Now.ToLongDateString() +
        " at " + DateTime.Now.ToLongTimeString();
        if( this.Sources != null && this.Sources.Count > 0 ) {
          header += "\n// Source" + (this.Sources.Count > 1 ? "s" : "") +
            " : " + string.Join(", ", this.Sources);
        }
      }
      return header;
    }

    private string GenerateReferences() {
      return @"using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.Diagnostics;
";
    }

    private string GenerateNamespace() {
      if(this.Namespace == null) { return null; }
      return "namespace " + this.Namespace + " {";
    }

    private string GenerateEntities() {
      return string.Join( "\n\n",
        this.Model.Entities.Select(x => this.GenerateEntity(x))
      );
    }

    private string GenerateEntity(Generator.Entity entity) {
      return string.Join( "",
        new List<string>() {
          this.GenerateSignature(entity),
          this.GenerateProperties(entity),
          this.GenerateConstructor(entity),
          this.GenerateToString(entity),
          this.GenerateEntityFooter(entity)
        }.Where(x => x != null)
      );
    }

    private string GenerateSignature(Generator.Entity entity) {
      return "public " +
        ( entity.IsVirtual ? "interface" : "class" ) + " " +
        this.PascalCase(entity.Name) + 
        (entity.Supers.Where(s=>s.IsVirtual).Count() > 0 ?
          " : " + string.Join( ", ",
            entity.Supers.Where(s=> s.IsVirtual).Select(x => this.PascalCase(x.Name))
          )
          : "") +
        " {";
    }

    private string GenerateProperties(Generator.Entity entity) {
      if(entity.IsVirtual) { return null; }
      return "\n" + string.Join("\n",
        entity.Properties.Select(x => this.GenerateProperty(x))
      );
    }

    private string GenerateProperty(Generator.Property property) {
      return "public " + this.GenerateType(property) + " " + 
        this.PascalCase(this.GeneratePropertyName(property)) + " { get; set; }";
    }

    private string GenerateType(Generator.Entity entity) {
      if( entity.Type == null ) { return "Object"; }
      if( entity.Type.Equals("<string>") ) { return "string"; }
      if( entity.Type.Equals("<bool>") ) { return "bool"; }
      return this.PascalCase(entity.Type);
    }

    private string GenerateType(Generator.Property property) {
      if(property.IsPlural) {
        return "List<" + this.PascalCase(property.Type) + ">";
      }
      if( property.Type == null ) { return "Object"; }
      if( property.Type.Equals("<string>") ) { return "string"; }
      if( property.Type.Equals("<bool>") ) { return "bool"; }
      return this.PascalCase(property.Type);
    }

    private string GenerateConstructor(Generator.Entity entity) {
      if(entity.IsVirtual) { return null; }
      if( ! entity.HasPluralProperty() ) { return null; }
      return "\n  public " + this.PascalCase(entity.Name) + "() {\n" +
        string.Join("\n",
          entity.Properties.Where(x => x.IsPlural).Select(x => 
            "    this." + this.PascalCase(this.GeneratePropertyName(x)) + " = new " + 
              this.GenerateType(x) + "();\n"
          )
        ) +
        "  }";
    }

    private string GenerateToString(Generator.Entity entity) {
      if(entity.IsVirtual) { return null; }
      return "\n  public override string ToString() {\n" +
        "    return\n" +
        "      \"" + this.PascalCase(entity.Name) + "(\" +\n" + 
        string.Join(" + \",\" +\n",
          entity.Properties.Select(x => this.GenerateToString(x))
        ) + ( entity.Properties.Count > 0 ? " + \n" : "" ) +
        "      \")\";\n" +
        "  }\n";
    }

    private string GenerateToString(Generator.Property property) {
      if(property.IsPlural) {
        return string.Format(
          "\"{0}=\" + \"[\" + \nstring.Join(\",\", " +
          "this.{0}.Select(x => x.ToString())) +\n" +
          "\"]\"",
          this.PascalCase(this.GeneratePropertyName(property))
        );
      } else {
        return string.Format(
          "        \"{0}=\" + this.{0}",
          this.PascalCase(this.GeneratePropertyName(property))
        );
      }
    }

    private string GenerateEntityFooter(Generator.Entity entity) {
      return "}";
    }

    // PARSER METHODS

    private string GenerateParsers() {
      return string.Join( "\n\n",
        this.GenerateParserHeader(),
        this.GenerateEntityParsers(),
        this.GenerateParserFooter()
      );
    }

    private string GenerateParserHeader() {
      return @"public class Parser : ParserBase {
public " + this.PascalCase(this.Model.Root.Name) + @" AST { get; set; }
public Parser Parse(string source) {
  this.Source = new Parsable(source);
  try {
    this.AST    = this.Parse" + this.PascalCase(this.Model.Root.Name) + @"();
  } catch(ParseException e) {
    this.Errors.Add(e);
    throw this.Source.GenerateParseException(""Failed to parse."");
  }
  if( ! this.Source.IsDone ) {
    throw this.Source.GenerateParseException(""Could not parse remaining data."");
  }
  return this;
}";
    }
  
    private string GenerateEntityParsers() {
      return string.Join("\n\n",
        this.Model.Entities
          .Where(entity => ! (entity.IsVirtual && entity.ParseAction is Generator.ConsumePattern))
          .Select(x => this.GenerateEntityParser(x))
      );
    }

    private string GenerateEntityParser(Generator.Entity entity) {
      return string.Join("\n\n",
        new List<string>() {
          this.GenerateEntityParserHeader(entity),
          this.GenerateParseAction(entity.ParseAction),
          this.GenerateEntityParserFooter(entity)
        }
      );
    }

    private string GenerateEntityParserHeader(Generator.Entity entity) {
      return "  public " + this.GenerateType(entity) + 
        " Parse" + this.PascalCase(entity.Name) + "() {\n" +
        string.Join("\n",
          entity.Properties.Select(x =>
            "    " + this.GenerateType(x) + " " + 
              this.GenerateLocalVariable(x) + 
            ( x.IsPlural ? " = new " + this.GenerateType(x) + "()" : " = " +
                (this.GenerateType(x).Equals("bool") ? "false" : "null")
            ) +
            ";"
          )
        ) + "\n\n" +
        "this.Log( \"Parse" + this.PascalCase(entity.Name) + "\" );\n" +
        "Parse( () => {\n";
    }

    private string GenerateParseAction(Generator.ParseAction action) {
      try {
        string code = new Dictionary<string, Func<Generator.ParseAction,string>>() {
          { "ConsumeString",  this.GenerateConsumeString  },
          { "ConsumePattern", this.GenerateConsumePattern },
          { "ConsumeEntity",  this.GenerateConsumeEntity  },
          { "ConsumeAll",     this.GenerateConsumeAll     },
          { "ConsumeAny",     this.GenerateConsumeAny     },
        }[action.GetType().ToString().Replace("HumanParserGenerator.Generator.","")](action);
        return this.WrapOptional(action, code);
      } catch(KeyNotFoundException e) {
        throw new NotImplementedException(
          "extracting not implemented for " + action.GetType().ToString(), e
        );
      }
    }

    private string GenerateConsumeString(Generator.ParseAction action) {
      Generator.ConsumeString consume = (Generator.ConsumeString)action;
      return this.GenerateAssignment(action) +
          ( consume.IsOptional ? "Maybe" : "" ) +
        "Consume(\"" + consume.String + "\");";
    }

    private string GenerateConsumePattern(Generator.ParseAction action) {
      Generator.ConsumePattern consume = (Generator.ConsumePattern)action;


      return this.GenerateAssignment(action) + "Consume(Extracting." +
        this.PascalCase(consume.Property.Entity.Name) + ");";
    }

    private string GenerateConsumeEntity(Generator.ParseAction action) {
      Generator.ConsumeEntity consume = (Generator.ConsumeEntity)action;
      if(consume.Property.IsPlural) {
        return this.GenerateLocalVariable(consume.Property) + 
          " = Many<" + this.GenerateType(consume.Entity) + ">(" + 
          this.GenerateConsumeSingleEntity(consume, true, true) +
          ");";
      }

      return this.GenerateConsumeSingleEntity(consume) + ";";
    }

    private string GenerateConsumeSingleEntity(Generator.ConsumeEntity consume,
                                               bool withoutAssignment = false,
                                               bool withoutExecution  = false)
    {
      // if the referenced Entity is Virtual and is an Extractor, consume it 
      // directly
      if(consume.Entity.IsVirtual && consume.Entity.ParseAction is Generator.ConsumePattern) {
        return (withoutAssignment ? "" : this.GenerateAssignment(consume) ) +
          "Consume(Extracting." + this.PascalCase(consume.Entity.Name) + ")";
      }

      // simple case, dispatch to Parse<Entity>
      return (withoutAssignment ? "" : this.GenerateAssignment(consume) ) +
        "Parse" + this.PascalCase(consume.Entity.Name) + 
          (withoutExecution ? "" : "()");
    }

    private string GenerateConsumeAll(Generator.ParseAction action) {
      Generator.ConsumeAll consume = (Generator.ConsumeAll)action;
      return string.Join("\n\n",
        consume.Actions.Select(next => this.GenerateParseAction(next))
      );
    }

    private string GenerateConsumeAny(Generator.ParseAction action) {
      Generator.ConsumeAny consume = (Generator.ConsumeAny)action;

      string code = "";
      bool first = true;
      foreach(var option in consume.Actions) {
        code +=
          (first ? "Parse" : ".Or") + "( () => { \n" +
            this.GenerateParseAction(option) + "\n" +
          "})\n";
        first = false;
      }
      code += ".OrThrow(\"Expected: " + consume.Label + "\");\n ";

      return code;
    }

    private string WrapOptional(Generator.ParseAction action, string code) {
      if( ! action.IsOptional )             { return code; }
      if( this.isTryConsumeString(action) ) { return code; }

      return "Maybe( () => {\n" + code + "\n});";
    }

    private string GenerateAssignment(Generator.ParseAction action) {
      if(action.Type == null)                                 { return ""; }
      if(action.Property == null)                             { return ""; }
      return this.GenerateLocalVariable(action.Property) + " = ";
    }

    private bool isTryConsumeString(Generator.ParseAction action) {
      return action.IsOptional &&
             action is HumanParserGenerator.Generator.ConsumeString;
    }

    private string GenerateLocalVariable(Generator.Property property) {
      string name = property.Name;
      // QnD solution to reserved words
      if( name.Equals("string") ) { return "text";     }
      if( name.Equals("int")    ) { return "number";   }
      if( name.Equals("float")  ) { return "floating"; }
      return this.CamelCase( name + this.PluralSuffix(property) );
    }

    private string GenerateEntityParserFooter(Generator.Entity entity) {
      return "})." +
        "OrThrow(\"Failed to parse " + this.PascalCase(entity.Name) + "\");\n" +
         this.GenerateEntityParserReturn(entity);
    }

    private string GenerateEntityParserReturn(Generator.Entity entity) {
      return entity.IsVirtual ?
        this.GenerateVirtualEntityParserReturn(entity) :
        this.GenerateRealEntityParserReturn(entity);
    }
    
    private string GenerateRealEntityParserReturn(Generator.Entity entity) {
      return "    return new " + this.PascalCase(entity.Name) + "()" +
        ( entity.Properties.Count > 0 ?
        "{\n" + 
        string.Join( ",\n",
          entity.Properties.Select(x =>
            "      " + this.PascalCase(this.GeneratePropertyName(x)) + " = " + 
              this.GenerateLocalVariable(x)
          )
        ) + "\n" +
        "}" : "") +
        ";\n}";
    }

    private string GenerateVirtualEntityParserReturn(Generator.Entity entity) {
      if(entity.Properties.Count > 0) {
      return "return " + this.GenerateLocalVariable(entity.Properties.First()) + ";\n" +
             "}"; 
      }
      return "return;"; 
    }

    // Extracting functionality is generated for all Entities that are "just"
    // consuming a pattern.
    private string GenerateExtracting() {
      return
        "public class Extracting {\n" +
        string.Join("\n",
          this.Model.Entities
                    .Where(entity => entity.ParseAction is Generator.ConsumePattern)
                    .Select(entity => 
                      "  public static Regex " + this.PascalCase(entity.Name) +
                        " = new Regex(@\"^" + ((Generator.ConsumePattern)entity.ParseAction).Pattern.Replace("\"", "\"\"") + "\");"
                    )
        ) + "\n" +
        "}";
    }

    private string GenerateParserFooter() {
      return @"
  [ConditionalAttribute(""DEBUG"")]
  private void Log(string msg) {
    Console.Error.WriteLine(""!!! "" + msg + "" @ "" + this.Source.Peek(10).Replace('\n', 'n'));
  }
}";
    }
    
    private string GenerateFooter() {
      string footer = "";
      footer += this.Namespace == null ? "" : "}";
      return footer;
    }

    // function to make sure that Properties don't have the same name as their
    // Class.
    // this is most of the time due to some recursion in a rule
    // e.g. rule ::= something [ rule ]
    private string GeneratePropertyName(Generator.Property property) {
      if(property.Name.Equals(property.Entity.Name)) {
        this.Warn("rewriting property name: " + property.Name);
        return "next-" + property.Name;
      }
      return property.Name + this.PluralSuffix(property);
    }

    // this function makes sure that text is correctly case'd ;-)
    // Dashes are removed and the first letter of each part is uppercased
    private string PascalCase(string text) {
      return string.Join("",
        text.Split('-').Select(x =>
          x.First().ToString().ToUpper() + x.ToLower().Substring(1)
        )
      );
    }

    private string CamelCase(string text) {
      var x = this.PascalCase(text);
      return x.First().ToString().ToLower() + x.Substring(1);
    }

    private void Warn(string msg) {
      Console.Error.WriteLine("~~~ C# Emitter Warning: " + msg);
    }

    private string PluralSuffix(Generator.Property property) {
      if(! property.IsPlural ) { return ""; }
      if( property.Name.EndsWith("x") ) { return "es"; }
      return "s";
    }

  }
}
