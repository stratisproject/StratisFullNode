// contracts/WrappedStrax.sol
// SPDX-License-Identifier: MIT
pragma solidity ^0.6.0;

import "https://github.com/OpenZeppelin/openzeppelin-contracts/blob/v3.3.0/contracts/access/Ownable.sol";
import "https://github.com/OpenZeppelin/openzeppelin-contracts/blob/v3.3.0/contracts/token/ERC20/ERC20.sol";

contract WrappedStraxToken is ERC20, Ownable {
    mapping (address => string) public withdrawalAddresses;
    
    constructor(uint256 initialSupply) public ERC20("WrappedStrax", "WSTRAX") {
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
    function burn(uint256 amount, string memory straxAddress) public {
        _burn(_msgSender(), amount);
        
        // When the tokens are burnt we need to know where to credit the equivalent value on the Stratis chain.
        // Currently it is only possible to assign a single address here, so if multiple recipients are required
        // the burner will have to wait until each burn is processed before proceeding with the next.
        withdrawalAddresses[msg.sender] = straxAddress;
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
    function burnFrom(address account, uint256 amount, string memory straxAddress) public {
        uint256 decreasedAllowance = allowance(account, _msgSender()).sub(amount, "ERC20: burn amount exceeds allowance");

        _approve(account, _msgSender(), decreasedAllowance);
        _burn(account, amount);
        
        // When the tokens are burnt we need to know where to credit the equivalent value on the Stratis chain.
        // Currently it is only possible to assign a single address here, so if multiple recipients are required
        // the burner will have to wait until each burn is processed before proceeding with the next.
        withdrawalAddresses[msg.sender] = straxAddress;
    }
}
