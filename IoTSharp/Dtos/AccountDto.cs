﻿using IoTSharp.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace IoTSharp.Dtos
{
    public class TokenEntity
    {
        /// <summary>
        /// token
        /// </summary>
        public string access_token { get; set; }
        /// <summary>
        /// 过期时间
        /// </summary>
        public int expires_in { get; set; }
    }

    public class LoginResult
    {
        /// <summary>
        /// 登录结果
        /// </summary>
        public ApiCode Code { get; set; }
        /// <summary>
        /// 用户名
        /// </summary>
        public string UserName { get; set; }
        /// <summary>
        /// 登录结果
        /// </summary>
        public Microsoft.AspNetCore.Identity.SignInResult SignIn { get; set; }
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Succeeded { get; set; }
        /// <summary>
        /// Token
        /// </summary>
        public TokenEntity Token { get; set; }
        /// <summary>
        /// 用户所具备权限
        /// </summary>
        public IList<string> Roles { get; set; }
    }

    public class LoginDto
    {
        /// <summary>
        /// 密码
        /// </summary>
        [Required]
        public string Password { get; set; }
        /// <summary>
        /// 用户名
        /// </summary>
        [Required]
        public string UserName { get; set; }
    }

    public class RegisterDto
    {
        /// <summary>
        /// 邮箱地址， 也是用户名
        /// </summary>
        [Required]
        public string Email { get; set; }
        /// <summary>
        /// 电话号码
        /// </summary>
        [Required]
        public string PhoneNumber { get; set; }
        /// <summary>
        /// 用户隶属客户ID
        /// </summary>
        [Required]
        public Guid CustomerId { get; set; }
        /// <summary>
        /// 用户名密码
        /// </summary>
        [Required]
        [StringLength(100, ErrorMessage = "PASSWORD_MIN_LENGTH", MinimumLength = 6)]
        public string Password { get; set; }
    }

    public class UserItemDto
    {
        public string Email { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
        public string PhoneNumber { get;  set; }
        public int AccessFailedCount { get;  set; }
        public string Id { get;  set; }
    }
}