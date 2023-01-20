// contracts/WrappedToken.sol
// SPDX-License-Identifier: MIT
pragma solidity ^0.6.0;

import "https://github.com/OpenZeppelin/openzeppelin-contracts/blob/v3.3.0/contracts/access/Ownable.sol";
import "https://github.com/OpenZeppelin/openzeppelin-contracts/blob/v3.3.0/contracts/token/ERC20/ERC20.sol";

contract WrappedToken is ERC20, Ownable {
    mapping (address => string) public withdrawalAddresses;
    mapping (address => bool) public _isBlacklisted;
    
    constructor(string tokenName, string tokenSymbol, uint256 initialSupply) public ERC20(tokenName, tokenSymbol) {
        _mint(msg.sender, initialSupply);
    }
    
    /**
     * @dev Creates `amount` new tokens and assigns them to `account`.
     *
     * See {ERC20-_mint}.
     */
    function mint(address account, uint256 amount) public onlyOwner {
        _mint(account, amount);
    }
    
    /**
     * @dev Destroys `amount` tokens from the caller.
     *
     * See {ERC20-_burn}.
     */
    function burn(uint256 amount, string memory tokenAddress, string memory burnId) public {
        _burn(_msgSender(), amount);
        
        // When the tokens are burnt we need to know where to credit the equivalent value on the Stratis chain.
        // Currently it is only possible to assign a single address here, so if multiple recipients are required
        // the burner will have to wait until each burn is processed before proceeding with the next.
        string memory key = string(abi.encodePacked(msg.sender, " ", burnId));
        withdrawalAddresses[key] = tokenAddress;
    }

    /**
     * @dev Destroys `amount` tokens from `account`, deducting from the caller's
     * allowance.
     *
     * See {ERC20-_burn} and {ERC20-allowance}.
     *
     * Requirements:
     *
     * - the caller must have allowance for ``accounts``'s tokens of at least
     * `amount`.
     */
    function burnFrom(address account, uint256 amount, string memory tokenAddress, string memory burnId) public {
        uint256 decreasedAllowance = allowance(account, _msgSender()).sub(amount, "ERC20: burn amount exceeds allowance");

        _approve(account, _msgSender(), decreasedAllowance);
        _burn(account, amount);
        
        // When the tokens are burnt we need to know where to credit the equivalent value on the Stratis chain.
        // Currently it is only possible to assign a single address here, so if multiple recipients are required
        // the burner will have to wait until each burn is processed before proceeding with the next.
        string memory key = string(abi.encodePacked(msg.sender, " ", burnId));
        withdrawalAddresses[key] = tokenAddress;
    }

    function addToBlackList(address[] calldata addresses) public onlyOwner {
        for (uint256 i; i < addresses.length; ++i) {
            _isBlacklisted[addresses[i]] = true;
        }
    }

    function removeFromBlackList(address account) public onlyOwner {
        _isBlacklisted[account] = false;
    }

     /**
     * @dev See {ERC20-_beforeTokenTransfer}.
     *
     * Requirements:
     *
     * - the addresses must not be blacklisted.
     */
    function _beforeTokenTransfer(address from, address to, uint256 amount) internal virtual override {
        super._beforeTokenTransfer(from, to, amount);

        require(!_isBlacklisted[from] && !_isBlacklisted[to], "This address is blacklisted");
    }
}
