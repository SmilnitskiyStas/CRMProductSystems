import { cancelTwoFactorChallenge } from '../twoFactorNavigation';

describe('cancelTwoFactorChallenge', () => {
  it('clears the challenge and returns to login without verification', () => {
    const actions = {
      clearChallenge: jest.fn(),
      resetVerification: jest.fn(),
      returnToLogin: jest.fn(),
    };

    expect(cancelTwoFactorChallenge(actions)).toBe(true);
    expect(actions.clearChallenge).toHaveBeenCalledTimes(1);
    expect(actions.resetVerification).toHaveBeenCalledTimes(1);
    expect(actions.returnToLogin).toHaveBeenCalledTimes(1);
  });
});
