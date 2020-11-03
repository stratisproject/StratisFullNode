FROM mcr.microsoft.com/dotnet/core/sdk:3.1

RUN git clone https://github.com/stratisproject/StratisFullNode.git \
    && cd /StratisFullNode/src/Stratis.StraxD \
	&& dotnet build

VOLUME /root/.stratisfullnode

WORKDIR /StratisFullNode/src/Stratis.StraxD

COPY strax.conf.docker /root/.stratisnode/strax/StraxTest/stratis.conf

EXPOSE 27103 27104 27105

CMD ["dotnet", "run", "-testnet"]
