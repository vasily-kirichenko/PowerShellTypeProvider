﻿namespace FSPowerShell

open System
open System.Collections.Generic
open System.Reflection
open System.IO
open System.Diagnostics
open System.Threading
open Samples.FSharp.ProvidedTypes
open Microsoft.FSharp.Core.CompilerServices
open System.Management.Automation
open System.Management.Automation.Runspaces
open FSPowerShell.PSRuntime


[<TypeProvider>]
type public PowerShellTypeProvider(cfg:TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces()

    // Get the assembly and namespace used to house the provided types
    let asm =  System.Reflection.Assembly.GetExecutingAssembly()
    let ns = "FSharp.PowerShell"
    let baseTy = typeof<obj>
    let staticParams = [ProvidedStaticParameter("PSSnapIns", typeof<string>); //, parameterDefaultValue = "")] // String list will be much better here
                        ProvidedStaticParameter("Is64BitRequired", typeof<bool>);]

    // Expose all available cmdlets as methods
    let shell = ProvidedTypeDefinition(asm, ns, "PowerShellTypeProvider", Some(baseTy))
    let helpText =
        """<summary>Typed representation of a PowerShell runspace</summary>
           <param name='PSSnapIns'>List of PSSnapIn that will be added at the start separated by semicolon.</param>
           <param name='Is64BitRequired'>Mark that 64bit runtime should be used for PowerShell</param>"""
    do shell.AddXmlDoc helpText
    do shell.DefineStaticParameters(
        parameters=staticParams,
        instantiationFunction=(fun typeName parameterValues ->
            let psSnapIns = parameterValues.[0] :?> string
            let is64bitRequired = parameterValues.[1] :?> bool

            let pty = ProvidedTypeDefinition(asm, ns, typeName, Some(baseTy))
            pty.AddMembersDelayed(fun() ->
               [let runtime = Manager.Current(psSnapIns.Split(';'), is64bitRequired, true)
                for cmdlet in runtime.AllCmdlets() do
                    let paramList = 
                       [for (name, ty) in cmdlet.ParametersInfo ->
                            let newTy, defValue = 
                                match ty with
                                | _ when ty = typeof<System.Management.Automation.SwitchParameter>->
                                    typeof<bool>, box false
                                | _ when ty.IsValueType ->
                                    ty, box None //System.Activator.CreateInstance(ty)
                                | _ -> ty, null
                            ProvidedParameter(PSInference.toCamelCase name, newTy, optionalValue=defValue)]
                    let paramCount = paramList.Length
                    let pm = 
                        ProvidedMethod(
                            methodName = cmdlet.Name,
                            parameters = paramList,
                            returnType = cmdlet.ResultType,
                            IsStaticMethod = true,
                            InvokeCode = 
                                fun args -> 
                                    if args.Length <> paramCount then
                                        failwithf "Expected %d arguments and received %d" paramCount args.Length
                                                 
                                    let namedArgs = [0..(paramCount-1)] |> List.map (fun i -> 
                                                        Quotations.Expr.Coerce(args.[i], typeof<obj>))
                                    let namedArgs = Quotations.Expr.NewArray(typeof<obj>, namedArgs)
                                    let rawName = cmdlet.RawName;
                                    
                                    <@@ Manager.Current(psSnapIns.Split(';'), is64bitRequired, false)
                                               .Execute(rawName, (%%namedArgs : obj[])) @@>)
                    pm.AddXmlDocDelayed(fun() ->runtime.GetXmlDoc(cmdlet.RawName))
                    yield pm :> MemberInfo
               ])
            pty))
    do this.AddNamespace(ns, [ shell ])

[<TypeProviderAssembly>]
do()