string mode = args.FirstOrDefault() ?? "maf";
if (mode is not ("maf" or "maf-simple" or "maf-simple-negative" or "prompt" or "negative" or "setup"))
{
    Console.Error.WriteLine("Mode must be maf, maf-simple, maf-simple-negative, prompt, negative, or setup.");
    return 2;
}

return await EvaluationRunner.RunAsync(mode);
