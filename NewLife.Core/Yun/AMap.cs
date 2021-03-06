﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewLife.Data;
using NewLife.Serialization;

namespace NewLife.Yun
{
    /// <summary>高德地图</summary>
    /// <remarks>
    /// 参考地址 http://lbs.amap.com/api/webservice/guide/api/georegeo/#geo
    /// </remarks>
    public class AMap : Map, IMap
    {
        #region 构造
        /// <summary>高德地图</summary>
        public AMap()
        {
            AppKey = "99ac084eb7dd8015fe0ff4404fa800da";
            KeyName = "key";
            //CoordType = "wgs84ll";
        }
        #endregion

        #region 方法
        /// <summary>远程调用</summary>
        /// <param name="url">目标Url</param>
        /// <param name="result">结果字段</param>
        /// <returns></returns>
        public override async Task<T> InvokeAsync<T>(String url, String result)
        {
            var dic = await base.InvokeAsync<IDictionary<String, Object>>(url, result);
            if (dic == null || dic.Count == 0) return default(T);

            var status = dic["status"].ToInt();
            if (status != 1) throw new Exception(dic["info"] + "");

            if (result.IsNullOrEmpty()) return (T)dic;

            return (T)dic[result];
        }
        #endregion

        #region 地址编码
        private String _geoUrl = "http://restapi.amap.com/v3/geocode/geo?address={0}&city={1}&output=json";
        /// <summary>查询地址的经纬度坐标</summary>
        /// <param name="address"></param>
        /// <param name="city"></param>
        /// <returns></returns>
        public async Task<IDictionary<String, Object>> GetGeocoderAsync(String address, String city = null)
        {
            if (address.IsNullOrEmpty()) throw new ArgumentNullException(nameof(address));

            var url = _geoUrl.F(address, city);

            var list = await InvokeAsync<IList<Object>>(url, "geocodes");
            return list.FirstOrDefault() as IDictionary<String, Object>;
        }

        /// <summary>查询地址获取坐标</summary>
        /// <param name="address">地址</param>
        /// <param name="city">城市</param>
        /// <param name="formatAddress">是否格式化地址。高德地图默认已经格式化地址</param>
        /// <returns></returns>
        public async Task<GeoAddress> GetGeoAsync(String address, String city = null, Boolean formatAddress = false)
        {
            var rs = await GetGeocoderAsync(address, city);
            if (rs == null || rs.Count == 0) return null;

            var gp = new GeoPoint();

            var ds = (rs["location"] + "").Split(",");
            if (ds != null && ds.Length >= 2)
            {
                gp.Longitude = ds[0].ToDouble();
                gp.Latitude = ds[1].ToDouble();
            }

            if (formatAddress) return await GetGeoAsync(gp);

            var addr = new GeoAddress();

            var reader = new JsonReader();
            reader.ToObject(rs, null, addr);

            addr.Code = rs["adcode"].ToInt();
            addr.Township = rs["township"] + "";
            addr.StreetNumber = rs["number"] + "";

            addr.Location = gp;

            return addr;
        }
        #endregion

        #region 逆地址编码
        private String _regeoUrl = "http://restapi.amap.com/v3/geocode/regeo?location={0},{1}&extensions=base&output=json";
        /// <summary>根据坐标获取地址</summary>
        /// <remarks>
        /// http://lbs.amap.com/api/webservice/guide/api/georegeo/#regeo
        /// </remarks>
        /// <param name="point"></param>
        /// <returns></returns>
        public async Task<IDictionary<String, Object>> GetGeocoderAsync(GeoPoint point)
        {
            if (point.Longitude < 0.1 || point.Latitude < 0.1) throw new ArgumentNullException(nameof(point));

            var url = _regeoUrl.F(point.Longitude, point.Latitude);

            return await InvokeAsync<IDictionary<String, Object>>(url, "regeocode");
        }

        /// <summary>根据坐标获取地址</summary>
        /// <param name="point"></param>
        /// <returns></returns>
        public async Task<GeoAddress> GetGeoAsync(GeoPoint point)
        {
            var rs = await GetGeocoderAsync(point);
            if (rs == null || rs.Count == 0) return null;

            var addr = new GeoAddress
            {
                Address = rs["formatted_address"] + ""
            };
            addr.Location = new GeoPoint
            {
                Longitude = point.Longitude,
                Latitude = point.Latitude
            };
            if (rs["addressComponent"] is IDictionary<String, Object> component)
            {
                var reader = new JsonReader();
                reader.ToObject(component, null, addr);

                addr.Code = component["adcode"].ToInt();
                addr.Township = component["town"] + "";

                if (rs["street_number"] is IDictionary<String, Object> sn && sn.Count > 0)
                {
                    addr.Street = sn["street"] + "";
                    addr.StreetNumber = component["street_number"] + "";
                }
            }

            addr.Location = point;

            return addr;
        }
        #endregion

        #region 路径规划
        private String _distanceUrl = "http://restapi.amap.com/v3/distance?origins={0},{1}&destination={2},{3}&type={4}&output=json";
        /// <summary>计算距离和驾车时间</summary>
        /// <remarks>
        /// http://lbs.amap.com/api/webservice/guide/api/direction
        /// 
        /// type:
        /// 0：直线距离
        /// 1：驾车导航距离（仅支持国内坐标）。
        /// 必须指出，当为1时会考虑路况，故在不同时间请求返回结果可能不同。
        /// 此策略和driving接口的 strategy = 4策略一致
        /// 2：公交规划距离（仅支持同城坐标）
        /// 3：步行规划距离（仅支持5km之间的距离）
        /// 
        /// distance    路径距离，单位：米
        /// duration    预计行驶时间，单位：秒
        /// </remarks>
        /// <param name="origin"></param>
        /// <param name="destination"></param>
        /// <param name="type">路径计算的方式和方法</param>
        /// <returns></returns>
        public async Task<Driving> GetDistanceAsync(GeoPoint origin, GeoPoint destination, Int32 type = 1)
        {
            if (origin == null || origin.Longitude < 1 && origin.Latitude < 1) throw new ArgumentNullException(nameof(origin));
            if (destination == null || destination.Longitude < 1 && destination.Latitude < 1) throw new ArgumentNullException(nameof(destination));

            var url = _distanceUrl.F(origin.Longitude, origin.Latitude, destination.Longitude, destination.Latitude, type);

            var list = await InvokeAsync<IList<Object>>(url, "results");
            if (list == null || list.Count == 0) return null;

            var geo = list.FirstOrDefault() as IDictionary<String, Object>;
            if (geo == null) return null;

            var rs = new Driving
            {
                Distance = geo["distance"].ToInt(),
                Duration = geo["duration"].ToInt()
            };

            return rs;
        }
        #endregion

        #region 行政区划
        //private String url3 = "http://restapi.amap.com/v3/config/district?keywords={0}&subdistrict={1}&filter={2}&extensions=all&output=json";
        private String _areaUrl = "http://restapi.amap.com/v3/config/district?keywords={0}&subdistrict={1}&filter={2}&extensions=base&output=json";
        /// <summary>行政区划</summary>
        /// <remarks>
        /// http://lbs.amap.com/api/webservice/guide/api/district
        /// </remarks>
        /// <param name="keywords"></param>
        /// <param name="subdistrict"></param>
        /// <param name="code"></param>
        /// <returns></returns>
        public async Task<IList<GeoArea>> GetDistrictAsync(String keywords, Int32 subdistrict = 1, Int32 code = 0)
        {
            if (keywords.IsNullOrEmpty()) throw new ArgumentNullException(nameof(keywords));

            var url = _areaUrl.F(keywords, subdistrict, code);

            var list = await InvokeAsync<IList<Object>>(url, "districts");
            if (list == null || list.Count == 0) return null;

            var geo = list.FirstOrDefault() as IDictionary<String, Object>;
            if (geo == null) return null;

            var addrs = GetArea(geo, 0);

            return addrs;
        }

        private IList<GeoArea> GetArea(IDictionary<String, Object> geo, Int32 parentCode)
        {
            if (geo == null || geo.Count == 0) return null;

            var addrs = new List<GeoArea>();

            var root = new GeoArea();
            new JsonReader().ToObject(geo, null, root);
            root.Code = geo["adcode"].ToInt();
            if (parentCode > 0) root.ParentCode = parentCode;

            addrs.Add(root);

            if (geo["districts"] is IList<Object> childs && childs.Count > 0)
            {
                foreach (var item in childs)
                {
                    if (item is IDictionary<String, Object> geo2)
                    {
                        var rs = GetArea(geo2, root.Code);
                        if (rs != null && rs.Count > 0) addrs.AddRange(rs);
                    }
                }
            }

            return addrs;
        }
        #endregion
    }
}