string mode = args.FirstOrDefault() ?? "maf";
if (mode is not ("maf" or "prompt" or "negative" or "setup"))
{
    Console.Error.WriteLine("Mode must be maf, prompt, negative, or setup.");
    return 2;
}

return await EvaluationRunner.RunAsync(mode);
