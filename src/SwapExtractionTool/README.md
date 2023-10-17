## Checking for Votes

To run the Extraction Tool to establish the current status of the StratisEVM Initiative vote, please follow the below steps.

 1. Run a StraxMain Node with the following parameters:

      `-txindex -addressindex`

2.  Wait for the node to become fully synchronised. 

**Note:** This may take some time, feel free to reach out in Discord to obtain a *near-tip* data directory if you do not want to validate the chain yourself.

 3.  Run this project with the StraxMain Node still running and speicfy the following parameter
	
	dotnet run -swapvote -startblock=1997000 -endblock=2023880
