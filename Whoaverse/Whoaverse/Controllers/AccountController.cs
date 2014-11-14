﻿/*
This source file is subject to version 3 of the GPL license, 
that is bundled with this package in the file LICENSE, and is 
available online at http://www.gnu.org/licenses/gpl.txt; 
you may not use this file except in compliance with the License. 

Software distributed under the License is distributed on an "AS IS" basis,
WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License for
the specific language governing rights and limitations under the License.

All portions of the code written by Whoaverse are Copyright (c) 2014 Whoaverse
All Rights Reserved.
*/

using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using Microsoft.Owin.Security;
using System;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Whoaverse.Models;
using Whoaverse.Models.ViewModels;
using Whoaverse.Utils;

namespace Whoaverse.Controllers
{
    [Authorize]
    public class AccountController : AsyncController
    {
        public AccountController()
            : this(new UserManager<WhoaVerseUser>(new UserStore<WhoaVerseUser>(new ApplicationDbContext())))
        {
            UserManager.UserValidator = new UserValidator<WhoaVerseUser>(UserManager) { AllowOnlyAlphanumericUserNames = false };
        }

        public AccountController(UserManager<WhoaVerseUser> userManager)
        {
            UserManager = userManager;

            // Configure user lockout defaults
            UserManager.UserLockoutEnabledByDefault = true;
            UserManager.DefaultAccountLockoutTimeSpan = TimeSpan.FromMinutes(5);
            UserManager.MaxFailedAccessAttemptsBeforeLockout = 5;
        }

        public UserManager<WhoaVerseUser> UserManager { get; private set; }

        // GET: /Account/Login
        [AllowAnonymous]
        public ActionResult Login(string returnUrl)
        {
            if (User.Identity.IsAuthenticated)
            {
                // deny access to registered users
                return RedirectToAction("Index", "Home");
            }

            ViewBag.SelectedSubverse = string.Empty;
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        // POST: /Account/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Login(LoginViewModel model, string returnUrl)
        {
            if (!ModelState.IsValid) return View(model);
            var user = await UserManager.FindAsync(model.UserName, model.Password);

            if (user == null)
            {

                // Check if correct username was entered with wrong password and increment failed attempts - lockout account
                var tmpuser = await UserManager.FindByNameAsync(model.UserName);
                if (tmpuser != null)
                {
                    await UserManager.AccessFailedAsync(tmpuser.Id);

                    // Check if correct username was entered and see if account was locked out, notify
                    if (await UserManager.IsLockedOutAsync(tmpuser.Id))
                    {
                        ModelState.AddModelError("", "This account has been locked out for security reasons. Try again later.");
                        return View(model);
                    }
                }

                ModelState.AddModelError("", "Invalid username or password.");
                return View(model);
            }

            // When token is verified correctly, clear the access failed count used for lockout
            await UserManager.ResetAccessFailedCountAsync(user.Id);

            // Sign in and continue
            await SignInAsync(user, model.RememberMe);

            // Read User Theme preference and set value to session variable
            Session["UserTheme"] = Utils.User.UserStylePreference(user.UserName);
            return RedirectToLocal(returnUrl);

            // If we got this far, something failed, redisplay form
        }

        // GET: /Account/Register
        [AllowAnonymous]
        public ActionResult Register()
        {
            if (User.Identity.IsAuthenticated)
            {
                //deny access to registered users
                return RedirectToAction("Index", "Home");
            }

            ViewBag.SelectedSubverse = string.Empty;
            return View();
        }

        // POST: /Account/Register
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);
            // begin recaptcha check
            //const string captchaMessage = "";
            //var isCaptchaCodeValid = ReCaptchaUtility.GetCaptchaResponse(captchaMessage, Request);

            //if (!isCaptchaCodeValid)
            //{
            //    ModelState.AddModelError("", "Incorrect recaptcha answer.");
            //    return View();
            //}
            // end recaptcha check

            try
            {
                var user = new WhoaVerseUser { UserName = model.UserName, RegistrationDateTime = DateTime.Now };

                var result = await UserManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    await SignInAsync(user, isPersistent: false);
                    // redirect new users to Welcome actionresult
                    return RedirectToAction("Welcome", "Home");
                }
                AddErrors(result);
            }
            catch (Exception)
            {
                ModelState.AddModelError(string.Empty, "Something bad happened. You broke Whoaverse.");
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        // POST: /Account/Disassociate
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Disassociate(string loginProvider, string providerKey)
        {
            var result = await UserManager.RemoveLoginAsync(User.Identity.GetUserId(), new UserLoginInfo(loginProvider, providerKey));
            ManageMessageId? message = result.Succeeded ? ManageMessageId.RemoveLoginSuccess : ManageMessageId.Error;
            return RedirectToAction("Manage", new { Message = message });
        }

        // GET: /Account/Manage
        public ActionResult Manage(ManageMessageId? message)
        {
            ViewBag.SelectedSubverse = string.Empty;
            ViewBag.userid = User.Identity.GetUserName();
            ViewBag.StatusMessage =
                message == ManageMessageId.ChangePasswordSuccess ? "Your password has been changed."
                : message == ManageMessageId.SetPasswordSuccess ? "Your password has been set."
                : message == ManageMessageId.RemoveLoginSuccess ? "The external login was removed."
                : message == ManageMessageId.Error ? "An error has occurred."
                : message == ManageMessageId.InvalidFileFormat ? "Please upload a .jpg or .png image."
                : message == ManageMessageId.UploadedFileToolarge ? "Uploaded file is too large. Current limit is 300 kb."
                : "";
            ViewBag.HasLocalPassword = HasPassword();
            ViewBag.ReturnUrl = Url.Action("Manage");
            return View();
        }

        // POST: /Account/Manage
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Manage(ManageUserViewModel model)
        {
            var hasPassword = HasPassword();
            ViewBag.HasLocalPassword = hasPassword;
            ViewBag.ReturnUrl = Url.Action("Manage");

            if (hasPassword)
            {
                if (!ModelState.IsValid) return View(model);
                var result = await UserManager.ChangePasswordAsync(User.Identity.GetUserId(), model.OldPassword, model.NewPassword);
                if (result.Succeeded)
                {
                    return RedirectToAction("Manage", new { Message = ManageMessageId.ChangePasswordSuccess });
                }
                AddErrors(result);
            }
            else
            {
                // User does not have a password so remove any validation errors caused by a missing OldPassword field
                var state = ModelState["OldPassword"];
                if (state != null)
                {
                    state.Errors.Clear();
                }

                if (!ModelState.IsValid) return View(model);
                var result = await UserManager.AddPasswordAsync(User.Identity.GetUserId(), model.NewPassword);
                if (result.Succeeded)
                {
                    return RedirectToAction("Manage", new { Message = ManageMessageId.SetPasswordSuccess });
                }
                AddErrors(result);
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        // POST: /Account/ExternalLogin
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public ActionResult ExternalLogin(string provider, string returnUrl)
        {
            // Request a redirect to the external login provider
            return new ChallengeResult(provider, Url.Action("ExternalLoginCallback", "Account", new { ReturnUrl = returnUrl }));
        }

        // GET: /Account/ExternalLoginCallback
        [AllowAnonymous]
        public async Task<ActionResult> ExternalLoginCallback(string returnUrl)
        {
            var loginInfo = await AuthenticationManager.GetExternalLoginInfoAsync();
            if (loginInfo == null)
            {
                return RedirectToAction("Login");
            }

            // Sign in the user with this external login provider if the user already has a login
            var user = await UserManager.FindAsync(loginInfo.Login);
            if (user != null)
            {
                await SignInAsync(user, isPersistent: false);
                return RedirectToLocal(returnUrl);
            }
            // If the user does not have an account, then prompt the user to create an account
            ViewBag.ReturnUrl = returnUrl;
            ViewBag.LoginProvider = loginInfo.Login.LoginProvider;
            return View("ExternalLoginConfirmation", new ExternalLoginConfirmationViewModel { UserName = loginInfo.DefaultUserName });
        }

        // POST: /Account/LinkLogin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LinkLogin(string provider)
        {
            // Request a redirect to the external login provider to link a login for the current user
            return new ChallengeResult(provider, Url.Action("LinkLoginCallback", "Account"), User.Identity.GetUserId());
        }

        // GET: /Account/LinkLoginCallback
        public async Task<ActionResult> LinkLoginCallback()
        {
            var loginInfo = await AuthenticationManager.GetExternalLoginInfoAsync(XsrfKey, User.Identity.GetUserId());
            if (loginInfo == null)
            {
                return RedirectToAction("Manage", new { Message = ManageMessageId.Error });
            }
            var result = await UserManager.AddLoginAsync(User.Identity.GetUserId(), loginInfo.Login);
            if (result.Succeeded)
            {
                return RedirectToAction("Manage");
            }
            return RedirectToAction("Manage", new { Message = ManageMessageId.Error });
        }

        // POST: /Account/ExternalLoginConfirmation
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ExternalLoginConfirmation(ExternalLoginConfirmationViewModel model, string returnUrl)
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Manage");
            }

            if (ModelState.IsValid)
            {
                // Get the information about the user from the external login provider
                var info = await AuthenticationManager.GetExternalLoginInfoAsync();
                if (info == null)
                {
                    return View("ExternalLoginFailure");
                }
                var user = new WhoaVerseUser { UserName = model.UserName };
                var result = await UserManager.CreateAsync(user);
                if (result.Succeeded)
                {
                    result = await UserManager.AddLoginAsync(user.Id, info.Login);
                    if (result.Succeeded)
                    {
                        await SignInAsync(user, isPersistent: false);
                        return RedirectToLocal(returnUrl);
                    }
                }
                AddErrors(result);
            }

            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }

        // POST: /Account/LogOff
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LogOff()
        {
            AuthenticationManager.SignOut();
            Session["UserTheme"] = "light";
            return RedirectToAction("Index", "Home");
        }

        // GET: /Account/ExternalLoginFailure
        [AllowAnonymous]
        public ActionResult ExternalLoginFailure()
        {
            return View();
        }

        [ChildActionOnly]
        public ActionResult RemoveAccountList()
        {
            try
            {
                var linkedAccounts = UserManager.GetLogins(User.Identity.GetUserId());
                ViewBag.ShowRemoveButton = HasPassword() || linkedAccounts.Count > 1;
                return PartialView("_RemoveAccountPartial", linkedAccounts);
            }
            catch (Exception)
            {
                return new EmptyResult();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && UserManager != null)
            {
                UserManager.Dispose();
                UserManager = null;
            }
            base.Dispose(disposing);
        }

        // POST: /Account/DeleteAccount
        [Authorize]
        [HttpPost]
        [PreventSpam(DelayRequest = 300, ErrorMessage = "Sorry, you are doing that too fast. Please try again later.")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteAccount(DeleteAccountViewModel model)
        {
            if (!ModelState.IsValid) return View("~/Views/Errors/Error.cshtml");
            if (!User.Identity.IsAuthenticated) return View("~/Views/Errors/Error.cshtml");
            AuthenticationManager.SignOut();

            // execute delete action
            return View(Utils.User.DeleteUser(User.Identity.Name) ? "~/Views/Account/AccountDeleted.cshtml" : "~/Views/Errors/Error.cshtml");
        }

        // GET: /Account/UserPreferencesAbout
        [ChildActionOnly]
        public ActionResult UserPreferencesAbout()
        {
            try
            {
                using (var db = new whoaverseEntities())
                {
                    var userPreferences = db.Userpreferences.Find(User.Identity.Name);

                    if (userPreferences != null)
                    {
                        // load existing preferences and return to view engine
                        var tmpModel = new UserAboutViewModel()
                        {
                            Shortbio = userPreferences.Shortbio,
                            Avatar = userPreferences.Avatar
                        };

                        return PartialView("_UserPreferencesAbout", tmpModel);
                    }
                    else
                    {
                        var tmpModel = new UserAboutViewModel();
                        return PartialView("_UserPreferencesAbout", tmpModel);
                    }
                }
            }
            catch (Exception)
            {
                return new EmptyResult();
            }
        }

        // POST: /Account/UserPreferences
        [Authorize]
        [HttpPost]
        [PreventSpam(DelayRequest = 15, ErrorMessage = "Sorry, you are doing that too fast. Please try again later.")]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> UserPreferencesAbout([Bind(Include = "Shortbio, Avatarfile")] UserAboutViewModel model)
        {
            // save changes
            using (var db = new whoaverseEntities())
            {
                var userPreferences = db.Userpreferences.Find(User.Identity.Name);
                var tmpModel = new Userpreference();

                if (userPreferences == null)
                {
                    // create a new record for this user in userpreferences table
                    tmpModel.Shortbio = model.Shortbio;
                    tmpModel.Username = User.Identity.Name;
                };

                if (model.Avatarfile != null && model.Avatarfile.ContentLength > 0)
                {
                    // check uploaded file size is < 300000 bytes (300 kilobytes)
                    if (model.Avatarfile.ContentLength < 300000)
                    {
                        try
                        {
                            using (var img = Image.FromStream(model.Avatarfile.InputStream))
                            {
                                if (img.RawFormat.Equals(ImageFormat.Jpeg) || img.RawFormat.Equals(ImageFormat.Png))
                                {
                                    // resize uploaded file
                                    ThumbGenerator.GenerateAvatar(img, User.Identity.Name, model.Avatarfile.ContentType);
                                    if (userPreferences == null)
                                    {
                                        tmpModel.Avatar = User.Identity.Name + ".jpg";
                                    }
                                    else
                                    {
                                        userPreferences.Avatar = User.Identity.Name + ".jpg";
                                    }
                                }
                                else
                                {
                                    // uploaded file was invalid
                                    ModelState.AddModelError("", "Uploaded file is not recognized as an image.");
                                    return RedirectToAction("Manage", new { Message = ManageMessageId.InvalidFileFormat });
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // uploaded file was invalid
                            ModelState.AddModelError("", "Uploaded file is not recognized as an image.");
                            return RedirectToAction("Manage", new { Message = ManageMessageId.InvalidFileFormat });
                        }
                    }
                    else
                    {
                        // refuse to save the file and explain why
                        ModelState.AddModelError("", "Uploaded image may not exceed 300 kb, please upload a smaller image.");
                        return RedirectToAction("Manage", new { Message = ManageMessageId.UploadedFileToolarge });
                    }
                }

                if (userPreferences == null)
                {
                    db.Userpreferences.Add(tmpModel);
                    await db.SaveChangesAsync();
                }
                else
                {
                    userPreferences.Shortbio = model.Shortbio;
                    userPreferences.Username = User.Identity.Name;
                    await db.SaveChangesAsync();
                }

            }

            return RedirectToAction("Manage");
        }

        // GET: /Account/UserPreferences
        [ChildActionOnly]
        public ActionResult UserPreferences()
        {
            try
            {
                using (var db = new whoaverseEntities())
                {
                    var userPreferences = db.Userpreferences.Find(User.Identity.Name);

                    if (userPreferences != null)
                    {
                        // load existing preferences and return to view engine
                        var tmpModel = new UserPreferencesViewModel
                        {
                            Disable_custom_css = userPreferences.Disable_custom_css,
                            Night_mode = userPreferences.Night_mode,
                            OpenLinksInNewTab = userPreferences.Clicking_mode,
                            Enable_adult_content = userPreferences.Enable_adult_content,
                            Public_subscriptions = userPreferences.Public_subscriptions,
                            Topmenu_from_subscriptions = userPreferences.Topmenu_from_subscriptions
                        };

                        return PartialView("_UserPreferences", tmpModel);
                    }
                    else
                    {
                        var tmpModel = new UserPreferencesViewModel();
                        return PartialView("_UserPreferences", tmpModel);
                    }
                }
            }
            catch (Exception)
            {
                return new EmptyResult();
            }
        }

        // POST: /Account/UserPreferences
        [Authorize]
        [HttpPost]
        [PreventSpam(DelayRequest = 15, ErrorMessage = "Sorry, you are doing that too fast. Please try again later.")]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> UserPreferences([Bind(Include = "Disable_custom_css, Night_mode, OpenLinksInNewTab, Enable_adult_content, Public_subscriptions, Topmenu_from_subscriptions, Shortbio, Avatar")] UserPreferencesViewModel model)
        {
            // save changes
            using (var db = new whoaverseEntities())
            {
                var userPreferences = db.Userpreferences.Find(User.Identity.Name);

                if (userPreferences != null)
                {
                    // modify existing preferences
                    userPreferences.Disable_custom_css = model.Disable_custom_css;
                    userPreferences.Night_mode = model.Night_mode;
                    userPreferences.Clicking_mode = model.OpenLinksInNewTab;
                    userPreferences.Enable_adult_content = model.Enable_adult_content;
                    userPreferences.Public_subscriptions = model.Public_subscriptions;
                    userPreferences.Topmenu_from_subscriptions = model.Topmenu_from_subscriptions;

                    await db.SaveChangesAsync();
                    // apply theme change
                    Session["UserTheme"] = Utils.User.UserStylePreference(User.Identity.Name);
                }
                else
                {
                    // create a new record for this user in userpreferences table
                    var tmpModel = new Userpreference
                    {
                        Disable_custom_css = model.Disable_custom_css,
                        Night_mode = model.Night_mode,
                        Clicking_mode = model.OpenLinksInNewTab,
                        Enable_adult_content = model.Enable_adult_content,
                        Public_subscriptions = model.Public_subscriptions,
                        Topmenu_from_subscriptions = model.Topmenu_from_subscriptions,
                        Username = User.Identity.Name
                    };
                    db.Userpreferences.Add(tmpModel);

                    await db.SaveChangesAsync();
                    // apply theme change
                    Session["UserTheme"] = Utils.User.UserStylePreference(User.Identity.Name);
                }
            }

            //return RedirectToAction("Manage", new { Message = "Your user preferences have been saved." });
            return RedirectToAction("Manage");
        }

        #region Helpers
        // Used for XSRF protection when adding external logins
        private const string XsrfKey = "XsrfId";

        private IAuthenticationManager AuthenticationManager
        {
            get
            {
                return HttpContext.GetOwinContext().Authentication;
            }
        }

        private async Task SignInAsync(WhoaVerseUser user, bool isPersistent)
        {
            AuthenticationManager.SignOut(DefaultAuthenticationTypes.ExternalCookie);
            var identity = await UserManager.CreateIdentityAsync(user, DefaultAuthenticationTypes.ApplicationCookie);
            AuthenticationManager.SignIn(new AuthenticationProperties { IsPersistent = isPersistent }, identity);
        }

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error);
            }
        }

        private bool HasPassword()
        {
            var user = UserManager.FindById(User.Identity.GetUserId());
            if (user != null)
            {
                return user.PasswordHash != null;
            }
            return false;
        }

        public enum ManageMessageId
        {
            ChangePasswordSuccess,
            SetPasswordSuccess,
            RemoveLoginSuccess,
            ChangePasswordAndRecoveryInfoSuccess,
            SetPasswordAndRecoveryInfoSuccess,
            ChangeRecoveryInfoSuccess,
            Error,
            InvalidFileFormat,
            UploadedFileToolarge
        }

        private ActionResult RedirectToLocal(string returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Index", "Home");
        }

        private class ChallengeResult : HttpUnauthorizedResult
        {
            public ChallengeResult(string provider, string redirectUri)
                : this(provider, redirectUri, null)
            {
            }

            public ChallengeResult(string provider, string redirectUri, string userId)
            {
                LoginProvider = provider;
                RedirectUri = redirectUri;
                UserId = userId;
            }

            public string LoginProvider { get; set; }
            public string RedirectUri { get; set; }
            public string UserId { get; set; }

            public override void ExecuteResult(ControllerContext context)
            {
                var properties = new AuthenticationProperties { RedirectUri = RedirectUri };
                if (UserId != null)
                {
                    properties.Dictionary[XsrfKey] = UserId;
                }
                context.HttpContext.GetOwinContext().Authentication.Challenge(properties, LoginProvider);
            }
        }
        #endregion
    }
}