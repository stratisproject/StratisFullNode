FROM microsoft/dotnet:3.1-sdk

RUN git clone https://github.com/stratisproject/StratisFullNode.git \
    && cd /StratisFullNode/src/Stratis.StraxD \
	&& dotnet build
	
VOLUME /root/.stratisfullnode

WORKDIR /StratisFullNode/src/Stratis.StraxD

COPY strax.conf.docker /root/.stratisnode/strax/StraxMain/stratis.conf

EXPOSE 17103 17104 17105

CMD ["dotnet", "run"]
