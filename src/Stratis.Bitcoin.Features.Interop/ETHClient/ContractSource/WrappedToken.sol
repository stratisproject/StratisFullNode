// SPDX-License-Identifier: MIT
pragma solidity ^0.6.0;

// Importing dependencies
import "https://github.com/OpenZeppelin/openzeppelin-contracts/blob/v3.3.0/contracts/access/Ownable.sol";
import "https://github.com/OpenZeppelin/openzeppelin-contracts/blob/v3.3.0/contracts/token/ERC20/ERC20.sol";
import "https://github.com/OpenZeppelin/openzeppelin-contracts/blob/v3.3.0/contracts/cryptography/ECDSA.sol";

contract WrappedToken is ERC20, Ownable {
    string public constant CONTRACT_NAME = "WrappedToken";
    string public constant CONTRACT_VERSION = "v1";

    mapping (string => string) public withdrawalAddresses;
    mapping (uint128 => bool) public uniqueNumberUsed;
    mapping (address => bool) public isBlacklisted;
    
    // Constructor initializes the ERC20 token
    constructor(string memory tokenName, string memory tokenSymbol, uint256 initialSupply) public ERC20(tokenName, tokenSymbol) {
        _mint(msg.sender, initialSupply);
    }
    
    // Allows only the owner to mint new tokens
    function mint(address account, uint256 amount) public onlyOwner {
        _mint(account, amount);
    }
    
    // Allows users to burn tokens and specify a withdrawal address
    function burn(uint256 amount, string memory tokenAddress, string memory burnId) public {
        _burn(msg.sender, amount);
        string memory key = string(abi.encodePacked(msg.sender, " ", burnId));
        withdrawalAddresses[key] = tokenAddress;
    }

    // Allows users to burn tokens from an approved address
    function burnFrom(address account, uint256 amount, string memory tokenAddress, string memory burnId) public {
        uint256 decreasedAllowance = allowance(account, msg.sender).sub(amount, "ERC20: burn amount exceeds allowance");
        _approve(account, msg.sender, decreasedAllowance);
        _burn(account, amount);
        string memory key = string(abi.encodePacked(msg.sender, " ", burnId));
        withdrawalAddresses[key] = tokenAddress;
    }

    // Allows the owner to add addresses to the blacklist
    function addToBlackList(address[] calldata addresses) external onlyOwner {
        for (uint256 i = 0; i < addresses.length; ++i) {
            isBlacklisted[addresses[i]] = true;
        }
    }

    // Allows the owner to remove addresses from the blacklist
    function removeFromBlackList(address account) external onlyOwner {
        isBlacklisted[account] = false;
    }

    // Checks if the address is blacklisted before any token transfer
    function _beforeTokenTransfer(address from, address to, uint256 amount) internal virtual override {
        super._beforeTokenTransfer(from, to, amount);
        require(!isBlacklisted[from] && !isBlacklisted[to], "This address is blacklisted");
    }
    
    // Perform a cross-chain transfer using delegated transfer with metadata
    function _transferForNetwork(
        address fromAddr,
        string memory targetNetwork,
        string memory targetAddress,
        string memory metadata,
        uint256 transferAmount
    ) internal {
        _beforeTokenTransfer(fromAddr, interflux, transferAmount);
        _transfer(fromAddr, interflux, transferAmount);
        emit CrossChainTransferLog(targetAddress, targetNetwork);
        emit MetadataLog(metadata);
    }

    function transferForNetwork(
        string memory targetNetwork,
        string memory targetAddress,
        string memory metadata,
        uint256 transferAmount
    ) public {
        _transferForNetwork(msg.sender, targetNetwork, targetAddress, metadata, transferAmount);
    }

    // Perform a cross-chain transfer using delegated transfer with metadata
    function delegatedTransferForNetwork(
        uint128 uniqueNumber, 
        address fromAddr,
        string memory targetNetwork,
        string memory targetAddress,
        string memory metadata,
        uint32 amount,
        uint8 amountCents,
        bytes memory signature
    ) public {    
        require(!uniqueNumberUsed[uniqueNumber], "Unique number already used");
        uniqueNumberUsed[uniqueNumber] = true;
        string memory token = symbol();
        bytes32 dataHash = keccak256(abi.encode(uniqueNumber, keccak256(bytes(token)), fromAddr, keccak256(bytes(targetNetwork)), keccak256(bytes(targetAddress)), keccak256(bytes(metadata)), amount, amountCents));
        bytes32 domainSeparator = _getDomainSeparator();
        bytes32 eip712DataHash = keccak256(abi.encodePacked("\x19\x01", domainSeparator, dataHash));
        address recoveredAddress = ECDSA.recover(eip712DataHash, signature);
        require(fromAddr == recoveredAddress, "The 'fromAddr' is not the signer");
        uint256 decimalsFactor = uint256(10) ** decimals();
        uint256 transferAmount = uint256(amount) * decimalsFactor + uint256(amountCents) * (decimalsFactor / 100);

        _transferForNetwork(fromAddr, targetNetwork, targetAddress, metadata, transferAmount);
    }

    // Event definitions
    event CrossChainTransferLog(string account, string network);
    event MetadataLog(string metadata);

    // Ethereum Interflux address variable
    address public interflux;

    // Method to update the Ethereum Interflux address
    function setInterflux(address newAddress) public onlyOwner {
        interflux = newAddress;
    }

    function _getDomainSeparator() internal view returns(bytes32 domainSeparator) {
        uint chainId;
        assembly {
            chainId := chainid()
        }
        
        return keccak256(abi.encode(keccak256("EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)"), keccak256(abi.encodePacked(CONTRACT_NAME)), keccak256(abi.encodePacked(CONTRACT_VERSION)), chainId, address(this)));
    }
}
